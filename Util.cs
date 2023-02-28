using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Octokit;
using Octokit.Internal;

namespace AddonScraper
{
    public abstract class Util
    {
        public static bool IsDefaultReadme(string readme)
        {
            if (string.IsNullOrEmpty(readme)) return false;
            return readme.Contains("Meteor Addon Template") &&
                   readme.Contains("A template to allow easy usage of the Meteor Addon API.");
        }
        
        // blacklist stuff
        private static List<string> _authorBlacklist;
        private static List<string> _idBlacklist;
        
        public static void LoadBlacklists()
        {
            var sw = new Stopwatch();
            sw.Start();
            _authorBlacklist = new List<string>();
            _idBlacklist = new List<string>();
            var authorBl = LoadStringList("author_blacklist");
            var idBl = LoadStringList("id_blacklist");
            if (authorBl == null || authorBl.Count < 1) Log("Unable to load author blacklist");
            else _authorBlacklist.AddRange(authorBl);
            if (idBl == null || idBl.Count < 1) Log("Unable to load repo id blacklist");
            else _idBlacklist.AddRange(idBl);
            sw.Stop();
            Log($@"Loaded blacklists in {sw.ElapsedMilliseconds}ms");
        }
        
        private static List<string> LoadStringList(string name)
        {
            if (!name.EndsWith(".txt")) name += ".txt";
            var path = Path.Combine(GetWorkDir(), name);
            if (File.Exists(path)) return File.ReadAllLines(path).ToList();
            Log($@"LoadStringList failed, invalid path: {path}");
            return null;
        }
        
        public static bool BadAuthor(string author)
        {
            return _authorBlacklist.Contains(author);
        }

        public static bool BadId(string id)
        {
            return _idBlacklist.Contains(id);
        }
        
        public static List<Repository> RemoveBlacklisted(IEnumerable<Repository> repos)
        {
            return (from repo in repos where !repo.Name.EndsWith("-addon-template") && !repo.Name.Equals("template") let author = repo.HtmlUrl.Split('/')[3] let id = $@"{author}/{repo.Name}" where !BadAuthor(author) && !BadId(id) select repo).ToList();
        }

        public static void Log(string m)
        {
            Console.WriteLine(m);
        }
        
        public static string GetWorkDir()
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private static string _githubKey;
        public static GitHubClient GetClient()
        {
            if (string.IsNullOrEmpty(_githubKey)) _githubKey = GetGithubKey();
            return string.IsNullOrEmpty(_githubKey) ? null : new GitHubClient(new ProductHeaderValue("meteor-addon-scraper"), new InMemoryCredentialStore(new Credentials(_githubKey)));
        }

        public static WebClient GetWebClient()
        {
            var c = new WebClient();
            c.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36");
            return c;
        }

        public static string GetGithubKey()
        {
            var keyf = Path.Combine(GetWorkDir(), "github_key.txt");
            return !File.Exists(keyf) ? null : File.ReadAllText(keyf);
        }
        
        public class AddonDatabase
        {
            [JsonProperty("database")]
            public List<MeteorAddon> Database { get; set; }
        }

        public class ErrorDatabase
        {
            [JsonProperty("database")]
            public List<ErrorMeta> Database { get; set; }
        }
        
        public class MeteorAddon
        {
            [JsonProperty("id")]
            public string Id { get; set; }
            
            [JsonProperty("name")]
            public string Name { get; set; }
            
            [JsonProperty("description")]
            public string Description { get; set; }
            
            [JsonProperty("author_list")]
            public List<string> Authors { get; set; }
            
            [JsonProperty("feature_list")]
            public List<string> Features { get; set; }
            
            [JsonProperty("meteor_version")]
            public string MeteorVersion { get; set; }
            
            [JsonProperty("minecraft_version")]
            public string MinecraftVersion { get; set; }
            
            [JsonProperty("icon_url")]
            public string IconUrl { get; set; }
            
            [JsonProperty("icon_b64")]
            public string CompressedIcon { get; set; }
            
            [JsonProperty("discord_url")]
            public string DiscordUrl { get; set; }
            
            [JsonProperty("repo_data")]
            public RepoMeta RepoMeta { get; set; }
            
            [JsonProperty("download_data")]
            public DownloadMeta DownloadMeta { get; set; }
        }

        public class ErrorMeta
        {
            [JsonProperty("repo_meta")]
            public RepoMeta RepoMeta { get; set; }
            
            [JsonProperty("id")]
            public string Id { get; set; }
            
            [JsonProperty("error")]
            public string Error { get; set; }
            
        }
        
        public class RepoMeta
        {
            [JsonProperty("url")]
            public string Url { get; set; }
            
            [JsonProperty("name")]
            public string Name { get; set; }
            
            [JsonProperty("author")]
            public string Author { get; set; }
            
            [JsonProperty("default_branch")]
            public string DefaultBranch { get; set; }

            public string GradlePropUrl()
            {
                return $@"https://raw.githubusercontent.com/{Author}/{Name}/{DefaultBranch}/gradle.properties";
            }

            public string FabricJsonUrl()
            {
                return $@"https://raw.githubusercontent.com/{Author}/{Name}/{DefaultBranch}/src/main/resources/fabric.mod.json";
            }

            public string ReadmeUrl()
            {
                return $@"https://raw.githubusercontent.com/{Author}/{Name}/{DefaultBranch}/README.md";
            }
            
        }

        public class DownloadMeta
        {
            [JsonProperty("release_url")]
            public string ReleaseUrl { get; set; }
            
            [JsonProperty("download_url")]
            public string DownloadUrl { get; set; }
            
            [JsonProperty("file_name")]
            public string FileName { get; set; }
            
            [JsonProperty("download_count")]
            public int DownloadCount { get; set; }
        }

        public class VersionMeta
        {
            public string Minecraft { get; set; }
            public string Meteor { get; set; }
        }
        
        public class Properties
        {
            private Dictionary<string, string> _data;

            public string Get(string field)
            {
                return _data.ContainsKey(field) ? _data[field] : null;
            }

            public string Get(string field, string defaultValue)
            {
                return Get(field) == null ? defaultValue : Get(field);
            }

            public void Set(string field, object value)
            {
                if (!_data.ContainsKey(field)) _data.Add(field, value.ToString());
                else _data[field] = value.ToString();
            }

            public Properties FromString(string propData)
            {
                if (string.IsNullOrEmpty(propData)) return null;
                _data = new Dictionary<string, string>();
                using (var sr = new StringReader(propData))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (!ShouldParse(line)) continue;
                        int index = line.IndexOf('=');
                        string key = line.Substring(0, index).Trim();
                        if (_data.ContainsKey(key)) continue;
                        string value = line.Substring(index + 1).Trim();
                        if ((value.StartsWith("\"") && value.EndsWith("\"")) || (value.StartsWith("'") && value.EndsWith("'"))) value = value.Substring(1, value.Length - 2);
                        _data.Add(key, value);
                    }
                }
                return this;
            }

            private static bool ShouldParse(string line)
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("'")) return false;
                return line.Contains("=");
            }
        }
        
        private static readonly Regex DiscRegex = new Regex(@"\bhttps:\/\/(?:www\.)?(?:discord\.(?:gg|com))\/invite\/[A-Za-z0-9]+(?:\?[^\s]*)?\b");

        public static string FindDiscordInvite(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var match = DiscRegex.Match(text);
            if (match.Success && !string.IsNullOrEmpty(match.Value)) return match.Value;
            return null;
        }
        
        
        // Image stuff
        public static Image ResizeImage(Image imgToResize, Size size)  
        {
            int sourceWidth = imgToResize.Width;
            int sourceHeight = imgToResize.Height;
            var nPercentW = size.Width / (float) sourceWidth;
            var nPercentH = size.Height / (float) sourceHeight;  
            var nPercent = nPercentH < nPercentW ? nPercentH : nPercentW;
            int destWidth = (int)(sourceWidth * nPercent);
            int destHeight = (int)(sourceHeight * nPercent);
            Bitmap b = new Bitmap(destWidth, destHeight); 
            Graphics g = Graphics.FromImage(b);  
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(imgToResize, 0, 0, destWidth, destHeight);  
            g.Dispose();
            return b;  
        }

        public static string ImageToB64(Image image, ImageFormat format)
        {
            using (var ms = new MemoryStream())
            {
                image.Save(ms, format);
                var bytes = ms.ToArray();
                return Convert.ToBase64String(bytes);
            }
        }
    }
}