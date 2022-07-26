namespace SlackHistoryLoader;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal sealed class Program
{
    private static readonly HttpClient httpClient = new();
    private static readonly StringBuilder uriBuilder = new();

    private const int MessagesCount = 100;
    private const string DownloadDirectory = "conversations";
    private const string BaseUrl = "https://slack.com/api/";
    private const string Token = "xoxp-XXXXXXXXXXX-XXXXXXXXXXX-XXXXXXXXXXXXX-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX";
    private const string ListUsersMethod = "users.list";
    private const string ListChannelsMethod = "conversations.list";
    private const string HistoryMethod = "conversations.history";
    private const string ChatDeleteMethod = "chat.delete";

    private static async Task Main()
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        Console.Write("Enter your Slack username: ");
        var username = Console.ReadLine();

        if (!Directory.Exists(DownloadDirectory))
        {
            Directory.CreateDirectory(DownloadDirectory);
        }

        var usersDict = await GetApiValuesAsync<string, string>(ListUsersMethod, "members", "id", "name");

        var userPair = usersDict.FirstOrDefault(u => u.Value == username);

        if (userPair.Value != username)
        {
            Console.WriteLine("Can't find user '" + username + "'");
            Console.Write("There are users: " + string.Join(Environment.NewLine, usersDict.Select(u => u.Value)));
            Console.ReadLine();
            return;
        }

        var userId = userPair.Key;

        var channelsDict = await GetApiValuesAsync<string, string>(ListChannelsMethod, "channels", "id", "name", "types=public_channel,private_channel,mpim");
        var imsDict = await GetApiValuesAsync<string, string>(ListChannelsMethod, "channels", "id", "user", "types=im");

        Console.WriteLine("Downloadind instant messages...");
        foreach (var conversation in imsDict)
        {
            await DownloadConversationHistoryAsync(false, conversation.Key, usersDict[conversation.Value], userId);
        }

        Console.WriteLine("Downloadind channel messages...");
        foreach (var channel in channelsDict)
        {
            await DownloadConversationHistoryAsync(true, channel.Key, channel.Value, userId);
        }
    }

    private static async Task DownloadConversationHistoryAsync(bool isChannel, string conversationId, string channelName, string userId, bool deleteMyMessages = false)
    {
        var oldest = "0";
        var hasMore = true;
        while (hasMore)
        {
            var history =
                await MakeApiCallAsync(
                    CombineApiUrl(
                        HistoryMethod,
                        "channel=" + conversationId,
                        "latest=" + oldest,
                        "count=" + MessagesCount.ToString()));

            var isOk = history["ok"].Value<bool>();
            if (!isOk)
            {
                Console.WriteLine("Error during loading a conversation history: " + history["error"]);
                break;
            }

            JToken messages = history["messages"];
            if (messages.Any())
            {
                await SaveHistoryToFileAsync(history, channelName + "_" + messages.First["ts"]?.Value<string>());
                Console.WriteLine("History from channel '" + channelName + "' saved. Messages: " + messages.Count().ToString());
            }
            else
            {
                break;
            }

            if (deleteMyMessages)
            {
                foreach (var message in messages)
                {
                    if (message["user"].Value<string>() == userId)
                    {
                        await MakeApiCallAsync(CombineApiUrl(ChatDeleteMethod, "channel=" + conversationId, "ts=" + message["ts"].Value<string>()));
                    }
                }
            }

            oldest = messages.Last["ts"].Value<string>();

            hasMore = history["has_more"].Value<bool>() && !history["is_limited"].Value<bool>();
        }
    }

    private static async Task SaveHistoryToFileAsync(JObject history, string fileName)
    {
        var path = Path.Combine(DownloadDirectory, fileName + ".json");
        using var streamWriter = File.CreateText(path);
        using var jsonWriter = new JsonTextWriter(streamWriter);
        await history.WriteToAsync(jsonWriter);
    }

    private static async Task<Dictionary<TKey, TValue>> GetApiValuesAsync<TKey, TValue>(string method, string path, string keyName, string valueName, params string[] parameters)
    {
        var obj = await MakeApiCallAsync(CombineApiUrl(method, parameters));
        var isOk = obj["ok"].Value<bool>();
        var ret = new Dictionary<TKey, TValue>();
        if (isOk)
        {
            foreach (var curValue in obj[path])
            {
                var id = curValue[keyName].Value<TKey>();
                var name = curValue[valueName].Value<TValue>();
                ret.Add(id, name);
            }
        }
        else
        {
            Console.WriteLine("Error during API call: " + obj["error"]);
        }

        return ret;
    }

    private static string CombineApiUrl(string method, params string[] parameters)
    {
        uriBuilder.Clear();
        uriBuilder
            .Append(BaseUrl)
            .Append(method);

        if (parameters is not null && parameters.Length > 0)
        {
            uriBuilder.Append('?');

            bool isFirst = true;
            foreach (var parameter in parameters ?? Array.Empty<string>())
            {
                if (isFirst)
                {
                    isFirst = false;
                }
                else
                {
                    uriBuilder.Append('&');
                }

                uriBuilder.Append(parameter);
            }
        }

        return uriBuilder.ToString();
    }

    private static async Task<JObject> MakeApiCallAsync(string apiUrl)
    {
        using var stream = await httpClient.GetStreamAsync(apiUrl);
        using var textReader = new StreamReader(stream);
        using var jsonReader = new JsonTextReader(textReader);
        return await JObject.LoadAsync(jsonReader);
    }
}
