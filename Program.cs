namespace SlackHistoryLoader
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;

    using Newtonsoft.Json.Linq;

    internal sealed class Program
    {
        private const int MessagesCount = 100;
        private const string DownloadDirectory = "talks";
        private const string BaseUrl = "https://slack.com/api/";
        private const string Token = "token=xoxp-16172012838-16167984613-102407978356-8fe5e0c5b36a6b1f4996a3c2bf44e1d1";
        private const string ListUsersMethod = "users.list";
        private const string ListChannelsMethod = "channels.list";
        private const string ListInstantMessagesChannelsMethod = "im.list";
        private const string InstantMessagesHistoryMethod = "im.history";
        private const string ChannelHistoryMethod = "channels.history";
        private const string ChatDeleteMethod = "chat.delete";

        private static void Main(string[] args)
        {
            Console.Write("Enter your Slack username: ");
            var username = Console.ReadLine();

            if (!Directory.Exists(DownloadDirectory))
            {
                Directory.CreateDirectory(DownloadDirectory);
            }

            var usersDict = GetApiValues<string, string>(ListUsersMethod, "members", "id", "name");

            if (usersDict.All(u => u.Value != username))
            {
                Console.WriteLine("Can't find user '" + username + "'");
                Console.Write("There are users: " + string.Join(Environment.NewLine, usersDict.Select(u => u.Value)));
                Console.ReadLine();
                return;
            }

            var xakepId = usersDict.First(u => u.Value == username).Key;

            var channelsDict = GetApiValues<string, string>(ListChannelsMethod, "channels", "id", "name");
            var imsDict = GetApiValues<string, string>(ListInstantMessagesChannelsMethod, "ims", "id", "user");

            Console.WriteLine("Downloadind instant messages...");
            foreach (var talk in imsDict)
            {
                DownloadTalkHistory(false, talk.Key, usersDict[talk.Value], xakepId);
            }

            Console.WriteLine("Downloadind channel messages...");
            foreach (var channel in channelsDict)
            {
                DownloadTalkHistory(true, channel.Key, channel.Value, xakepId);
            }
        }

        private static void DownloadTalkHistory(bool isChannel, string talkId, string channelName, string userId, bool deleteMyMessages = false)
        {
            var oldest = "0";
            var hasMore = true;
            while (hasMore)
            {
                var history =
                    MakeApiCall(
                        CombineApiUrl(
                            isChannel ? ChannelHistoryMethod : InstantMessagesHistoryMethod,
                            "channel=" + talkId,
                            "latest=" + oldest,
                            "count=" + MessagesCount.ToString()));

                var isOk = history["ok"].Value<bool>();
                if (!isOk)
                {
                    Console.WriteLine(history["error"]);
                    break;
                }

                if (history["messages"].Any())
                {
                    SaveHistoryToFile(history, channelName + "_" + history["messages"].First["ts"]?.Value<string>());
                    Console.WriteLine("History from channel '" + channelName + "' saved. Messages: " + history["messages"].Count().ToString());
                }
                else
                {
                    break;
                }

                if (deleteMyMessages)
                {
                    foreach (var message in history["messages"])
                    {
                        if (message["user"].Value<string>() == userId)
                        {
                            MakeApiCall(CombineApiUrl(ChatDeleteMethod, "channel=" + talkId, "ts=" + message["ts"].Value<string>()));
                        }
                    }
                }

                oldest = history["messages"].Last["ts"].Value<string>();

                hasMore = history["has_more"].Value<bool>() && !history["is_limited"].Value<bool>();
            }
        }

        private static void SaveHistoryToFile(JObject history, string fileName)
        {
            using (var file = File.CreateText(Path.Combine(DownloadDirectory, fileName + ".json")))
            {
                file.Write(history.ToString());
            }
        }

        private static Dictionary<TKey, TValue> GetApiValues<TKey, TValue>(string method, string path, string keyName, string valueName)
        {
            var obj = MakeApiCall(CombineApiUrl(method));
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
                Console.WriteLine(obj["error"]);
            }

            return ret;
        }

        private static string CombineApiUrl(string method, params string[] parameters)
        {
            var sb = new StringBuilder(BaseUrl);
            sb.Append(method)
                .Append('?')
                .Append(Token);

            foreach (var parameter in parameters ?? Array.Empty<string>())
            {
                sb.Append('&').Append(parameter);
            }

            return sb.ToString();
        }

        private static JObject MakeApiCall(string apiUrl)
        {
            var request = WebRequest.Create(apiUrl) as HttpWebRequest;
            var response = request?.GetResponse() as HttpWebResponse;

            if (response == null)
            {
                return null;
            }

            using (var stream = response.GetResponseStream())
            {
                if (stream == null)
                {
                    return null;
                }

                using (var stringStream = new StreamReader(stream))
                {
                    return JObject.Parse(stringStream.ReadToEnd());
                }
            }
        }
    }
}
