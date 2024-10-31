using System;
using StackExchange.Redis;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.Shared
{
    public class SettingsService
    {
        private ConnectionMultiplexer con;
        private ISettingsApi api;
        public ConnectionMultiplexer Con => con;
        public SettingsService(IConfiguration config, ILogger<SettingsService> logger, ISettingsApi api)
        {
            var redis = config["SETTINGS_REDIS_HOST"];
            if (redis == null)
                logger.LogWarning("SETTINGS_REDIS_HOST is not set, settings updates will not be received");
            else
                con = ConnectionMultiplexer.Connect(redis);
            this.api = api;
        }

        public async Task<ChannelMessageQueue> GetAndSubscribe<T>(string userId, string key, Action<T> update, Func<T> defaultGetter = null)
        {
            for (int i = 0; i < 3; i++)
                try
                {
                    // subscribe, get then process subscription to not loose any update
                    var subTask = con?.GetSubscriber().SubscribeAsync(GetSubKey(userId, key));
                    T val = await GetCurrentValue(userId, key, defaultGetter);
                    update(val);

                    if (con == null)
                        return null;

                    var sub = await subTask.ConfigureAwait(false);
                    sub.OnMessage(a =>
                    {
                        update(Deserialize<T>(a.Message));
                    });
                    return sub;
                }
                catch (Exception e)
                {
                    if (i == 2)
                        throw new Exception("Could not subscribe to setting ", e);
                    await Task.Delay(150 * (i + 1));
                }
            throw new Exception("Could not subscribe to setting (should not be reached)");
        }

        public async Task<T> GetCurrentValue<T>(string userId, string key, Func<T> defaultGetter)
        {
            string value = null;
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var response = await api.SettingsGetSettingWithHttpInfoAsync(userId, key);
                    if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                        return DefaultFor(defaultGetter);
                    value = response.Data;
                }
                catch (System.Exception)
                {
                    if (i == 2)
                        throw;
                }
                if (value != null)
                    break;

                await Task.Delay(150 * (i + 1));
            }
            T val = Deserialize<T>(value);
            return val;
        }

        public static T DefaultFor<T>(Func<T> defaultGetter)
        {
            if (defaultGetter != null)
                return defaultGetter();
            else
                return default(T);
        }

        private static string GetSubKey(string userId, string key)
        {
            return userId + key;
        }

        public async Task UpdateSetting<T>(string userId, string key, T data)
        {
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    await api.SettingsUpdateSettingAsync(userId, key, JsonConvert.SerializeObject(JsonConvert.SerializeObject(data)), 0, new System.Threading.CancellationTokenSource(3000).Token);
                    return;
                }
                catch (Exception e)
                {
                    await Task.Delay(500 * ((i + 1) * (i + 1)));
                    if (i > 0)
                        Console.WriteLine($"failed to update settings {e.Message} \n" + JsonConvert.SerializeObject(data));
                    if (i == 3)
                        throw;
                }
            }
        }

        private static T Deserialize<T>(string a)
        {
            if (a.StartsWith("\""))
                a = JsonConvert.DeserializeObject<string>(a);
            return JsonConvert.DeserializeObject<T>(a);
        }
    }
}
