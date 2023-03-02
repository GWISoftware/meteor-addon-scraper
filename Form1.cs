using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json;
using Octokit;

namespace AddonScraper
{
    public partial class Form1 : Form
    {
        
        public Form1()
        {
            InitializeComponent();
        }

        private void Log(string s)
        {
            logBox.Text += s + "\n";
            logBox.SelectionStart = logBox.Text.Length + 1;
            logBox.ScrollToCaret();
            Update();
        }

        private void SetTitle(string s)
        {
            Text = s;
            Update();
        }
        
        private async void scrapeNow_Click(object sender, EventArgs e)
        {
            var client = Util.GetClient();
            var request = MakeSearchReq();
            pgBar.Value = 0;
            
            SetTitle("Requesting repository search");
            var result = await client.Search.SearchRepo(request);
            if (result == null)
            {
                Log("Repository search failed, cancelling.");
                return;
            }
            
            SetTitle("Processing repository search");
            if (result.IncompleteResults) Log("[!] This search may contain incomplete results");
            Log($@"Search returned {result.Items.Count} results");
            var results = new List<Util.MeteorAddon>();
            var errors = new List<Util.ErrorMeta>();
            var repos = Util.RemoveBlacklisted(result.Items); // good idea to blacklist outdated/bad search results to improve speed ahead of time
            Log($@"Removed {result.Items.Count - repos.Count} results from blacklist");
            Log($@"Processing {repos.Count} results.");

            var toCheck = repos.Count;
            var rchecked = 0;
            pgBar.Maximum = repos.Count;
            
            foreach (var repo in repos)
            {
                pgBar.Value++;
                rchecked++;
                SetTitle($@"Checking {repo.Name} ({rchecked}/{toCheck})");
                Thread.Sleep(2500);

                var addon = BaseAddon();
                var repoMeta = addon.RepoMeta;

                repoMeta.Name = repo.Name;
                repoMeta.Url = repo.HtmlUrl;
                repoMeta.Author = /*repo.Owner.Name;*/ repoMeta.Url.Split('/')[3]; // no idea why repo.Owner.Name is empty?
                repoMeta.DefaultBranch = repo.DefaultBranch;
                addon.Id = $@"{repoMeta.Author}/{repoMeta.Name}";
                
                DiscordInvCheck(addon, repo); // check for invite links in repo meta
                
                var readme = GetReadmeData(addon);
                Thread.Sleep(1500);
                
                if (string.IsNullOrEmpty(readme))
                {
                    errors.Add(MakeErrorMeta(addon, "No readme"));
                    continue;
                }

                if (Util.IsDefaultReadme(readme))
                { // check for stuff that still has the default readme
                    Log("Repo is an unmodified template clone, skipping");
                    errors.Add(MakeErrorMeta(addon, "Unused clone/fork"));
                    continue;
                }

                if (string.IsNullOrEmpty(addon.DiscordUrl))
                { // look for discord invite in readme if it wasn't found before
                    var inv = Util.FindDiscordInvite(readme);
                    if (!string.IsNullOrEmpty(inv))
                    {
                        Log($@"Found discord link in readme: {inv}");
                        addon.DiscordUrl = inv;
                    }
                }
                
                var propData = GetPropData(addon); // get gradle.properties
                if (string.IsNullOrEmpty(propData))
                {
                    Log("Invalid gradle properties data, skipping.");
                    errors.Add(MakeErrorMeta(addon, "Error getting gradle.properties data"));
                    continue;
                }

                var fabricData = GetFabricData(addon); // get fabric.mod.json
                if (string.IsNullOrEmpty(fabricData))
                {
                    Log("Invalid fabric.mod.json data, skipping.");
                    errors.Add(MakeErrorMeta(addon, "Error getting fabric.mod.json data"));
                    continue;
                }
                
                var fabricJson = JsonConvert.DeserializeObject<dynamic>(fabricData);
                SetAuthors(addon, fabricJson);
                if (addon.Authors.Any(Util.BadAuthor))
                { // check for blacklisted authors in fabric.mod.json author list
                    Log("One or more project authors is blacklisted, skipping");
                    errors.Add(MakeErrorMeta(addon, "Blacklisted author in fabric json author list"));
                    continue;
                }

                var vm = GetVersionMeta(propData); // parse gradle.properties -> minecraft & meteor version
                if (vm == null)
                {
                    errors.Add(MakeErrorMeta(addon, "Error with gradle.properties"));
                    continue;
                }

                if (IsOutdatedVer(vm.Minecraft)) // check minecraft version
                {
                    Log("Addon is on an outdated minecraft version, skipping");
                    errors.Add(MakeErrorMeta(addon, "Outdated minecraft version"));
                    continue;
                }
                
                addon.MinecraftVersion = vm.Minecraft;
                addon.MeteorVersion = vm.Meteor;

                Release latestRelease;
                try
                { // get latest release
                    latestRelease =
                        await client.Repository.Release.GetLatest(addon.RepoMeta.Author, addon.RepoMeta.Name);
                    Thread.Sleep(1000);
                }
                catch (ApiException api1)
                {
                    Util.Log(api1.Message.Equals("Not Found")
                        ? "No release available."
                        : $@"Api exception trying to get latest release : {api1.Message}");
                    Log("Couldn't find a release, skipping.");
                    errors.Add(MakeErrorMeta(addon, "No release available"));
                    continue;
                }

                var asset = GetCorrectAsset(latestRelease); // find the correct jar (not -dev -sources etc)s
                if (asset == null)
                {
                    Log("Couldn't find a proper asset in the latest release, skipping.");
                    errors.Add(MakeErrorMeta(addon, "No valid release asset available."));
                    continue;
                }
                
                SetDlMeta(addon, asset, latestRelease); // set download info
                
                if (fabricJson.entrypoints == null || fabricJson.entrypoints.meteor == null)
                { // sanity check
                    Log("Unable to find entrypoint, skipping.");
                    errors.Add(MakeErrorMeta(addon, "Invalid or improper entrypoint"));
                    continue;
                }

                addon.Name = fabricJson.name != null ? (string) fabricJson.name.ToString() : repoMeta.Name; // addon name
                    
                string mainClass = fabricJson.entrypoints.meteor[0].ToString();
                var mainClassName = mainClass.Split('.').Last();
                var entrypoint = mainClass.Replace($@".{mainClassName}", "");
                var mainClassData = GetMainClassData(addon, mainClass); // get main class data
                if (string.IsNullOrEmpty(mainClassData))
                {
                    Log("Bad data for main class, skipping.");
                    errors.Add(MakeErrorMeta(addon, "Main class data is invalid"));
                    continue;
                }
                
                addon.Features = new List<string>(); // feature list
                var fl = GetFeaturesFromClass(mainClassData); // (hopefully) check main class for module list first
                if (fl.Count <= 0)
                {
                    Log("No features were detected in the main class, trying to scrape module list from repo");
                    var moduleRoot = $@"src/main/java/{entrypoint.Replace('.', '/')}/modules";
                    Log($@"Expected modules path: {moduleRoot}");
                    try
                    {
                        var rootc = await client.Repository.Content.GetAllContents(addon.RepoMeta.Author,
                            addon.RepoMeta.Name, moduleRoot); // content list for modules root
                        Thread.Sleep(1000);
                        var moduleFs =
                            rootc.Where(item => item.Type.Equals(ContentType.Dir))
                                .ToList(); // get all folders from module root
                        var moduleList =
                            (from f in moduleFs
                                where f.Type.Equals(ContentType.File) && f.Name.EndsWith(".java")
                                select f.Name.Replace(".java", "")).ToList(); // collect top-level modules first
                        Util.Log($@"Found {moduleList.Count} top-level modules, and {moduleFs.Count} module folders");
                        Thread.Sleep(1000);

                        foreach (var mf in moduleFs)
                        {
                            // collect modules from each (category) folder
                            Log($@"Scanning {mf.Path} for modules");
                            var contents = await client.Repository.Content.GetAllContents(addon.RepoMeta.Author,
                                addon.RepoMeta.Name, mf.Path);
                            moduleList.AddRange(from moduleFile in contents
                                where moduleFile.Type.Equals(ContentType.File) && moduleFile.Name.EndsWith(".java")
                                select moduleFile.Name.Replace(".java", ""));
                            Thread.Sleep(1000);
                        }

                        Log($@"Scan returned a total of {moduleList.Count} modules");
                        addon.Features = moduleList;
                    }
                    catch (Exception e2)
                    {
                        Log("Exception trying to scrape module list, check logs.");
                        Util.Log($@"Error trying to scrape modules for {addon.Name} : {e2.Message}");
                    }
                }
                addon.Features.AddRange(fl);
                addon.Features.Sort();
                Log(addon.Features.Count == 0 ? @"No features were found." : $@"Found {addon.Features.Count} features");
                
                addon.Description = fabricJson.description != null ? (string) fabricJson.description.ToString() : "A Meteor Client Addon"; // description
                SetIcon(addon, fabricJson); // icon
                SaveIcon(addon);
                Thread.Sleep(1500);
                results.Add(addon);
            }

            var database = new Util.AddonDatabase { Database = results }; // databases for shop items and errors for review
            var errorDb = new Util.ErrorDatabase { Database = errors };

            SetTitle("Saving results");
            Log("Saving results to file...");
            var outf = Path.Combine(Util.GetWorkDir(), "database.json");
            var eoutf = Path.Combine(Util.GetWorkDir(), "error_database.json");
            var sw = new Stopwatch();
            sw.Start();
            using (var file = File.CreateText(outf))
            {
                using (var writer = new JsonTextWriter(file)) await writer.WriteRawAsync(JsonConvert.SerializeObject(database, Formatting.Indented));
                sw.Stop();
                Log($@"Saved {database.Database.Count} items to shop database in {sw.ElapsedMilliseconds}ms");
            }
            using (var file = File.CreateText(eoutf))
            {
                sw.Restart();
                using (var writer = new JsonTextWriter(file)) await writer.WriteRawAsync(JsonConvert.SerializeObject(errorDb, Formatting.Indented));
                sw.Stop();
                Log($@"Saved {errorDb.Database.Count} items to error database in {sw.ElapsedMilliseconds}ms");
            }
        }
        
        private static SearchRepositoriesRequest MakeSearchReq()
        {
            var request = new SearchRepositoriesRequest("meteor addon")
            { 
                Language = Language.Java, // kotlin = cope
                Archived = false, // ignore archived repos
                In = new[] { InQualifier.Readme, InQualifier.Description }, // search both for matching repos
                Updated = DateRange.GreaterThan(DateTimeOffset.Now.AddMonths(-3)) // filter out old/dead repos
            };
            return request;
        }

        private static Util.MeteorAddon BaseAddon()
        {
            return new Util.MeteorAddon 
            {
                RepoMeta = new Util.RepoMeta(),
                DownloadMeta = new Util.DownloadMeta()
            };
        }

        private void DiscordInvCheck(Util.MeteorAddon addon, Repository repo)
        {
            if (!string.IsNullOrEmpty(repo.Homepage))
            {
                var inv = Util.FindDiscordInvite(repo.Homepage);
                if (!string.IsNullOrEmpty(inv))
                {
                    Log($@"Found discord url from repo homepage: {inv}");
                    addon.DiscordUrl = inv;
                    return;
                }
            }
            if (string.IsNullOrEmpty(repo.Description)) return;
            var inv2 = Util.FindDiscordInvite(repo.Description);
            if (string.IsNullOrEmpty(inv2)) return;
            Log($@"Found discord url from repo description: {inv2}");
            addon.DiscordUrl = inv2;
        }

        private static Util.ErrorMeta MakeErrorMeta(Util.MeteorAddon addon, string reason)
        {
            var em = new Util.ErrorMeta
            {
                Error = reason,
                Id = addon.Id,
                RepoMeta = addon.RepoMeta
            };
            return em;
        }

        private string GetPropData(Util.MeteorAddon addon)
        {
            using (var client = Util.GetWebClient())
            {
                try
                {
                    var propData = client.DownloadString(addon.RepoMeta.GradlePropUrl());
                    return propData;
                }
                catch (WebException re)
                {
                    if (re.Response is HttpWebResponse resp && resp.StatusCode == HttpStatusCode.NotFound) Log("gradle.properties doesn't exist in repo, skipping");
                    else Log($@"Web Exception trying to fetch gradle.properties: {re.Message}");
                    return null;
                }
            }
        }

        private string GetFabricData(Util.MeteorAddon addon)
        {
            using (var client = Util.GetWebClient())
            {
                try
                {
                    var fabData = client.DownloadString(addon.RepoMeta.FabricJsonUrl());
                    return fabData;
                }
                catch (WebException re)
                {
                    if (re.Response is HttpWebResponse resp && resp.StatusCode == HttpStatusCode.NotFound) Log("fabric.mod.json doesn't exist in repo, skipping");
                    else Log($@"Web Exception trying to fetch fabric.mod.json: {re.Message}");
                    return null;
                }
            }
        }

        private string GetReadmeData(Util.MeteorAddon addon)
        {
            using (var client = Util.GetWebClient())
            {
                try
                {
                    var readmeData = client.DownloadString(addon.RepoMeta.ReadmeUrl());
                    return readmeData;
                }
                catch (WebException re)
                {
                    if (re.Response is HttpWebResponse resp && resp.StatusCode == HttpStatusCode.NotFound) Log("Repo doesn't have a readme? skipping");
                    else Log($@"Web Exception trying to fetch fabric.mod.json: {re.Message}, skipping");
                    return null;
                }
            }
        }

        private Util.VersionMeta GetVersionMeta(string propData)
        {
            if (string.IsNullOrEmpty(propData)) return null;
            var meta = new Util.Properties().FromString(propData);
            if (meta == null)
            {
                Log("Unable to parse gradle.properties, skipping.");
                return null;
            }

            var mc = meta.Get("minecraft_version");
            var meteor = meta.Get("meteor_version");
            if (!string.IsNullOrEmpty(mc) && !string.IsNullOrEmpty(meteor))
                return new Util.VersionMeta
                {
                    Meteor = meteor,
                    Minecraft = mc
                };
            Log("Unable to find versions, skipping.");
            return null;
        }


        private static string GetMainClassData(Util.MeteorAddon addon, string mainClass)
        {
            var mainClassUrl =
                $@"https://raw.githubusercontent.com/{addon.RepoMeta.Author}/{addon.RepoMeta.Name}/{addon.RepoMeta.DefaultBranch}/src/main/java/{mainClass.Replace(".", "/")}.java";
            using (var client = Util.GetWebClient())
            {
                try { return client.DownloadString(mainClassUrl); }
                catch (Exception e)
                {
                    Util.Log(mainClassUrl);
                    Util.Log($@"Error requesting main class : {e.Message}");
                    return null;
                }
            }
        }

        private static List<string> GetFeaturesFromClass(string classData)
        {
            var regex = new Regex(@"(?:add\(new )([^(]+)(?:\([^)]*)\)\)");
            var m = regex.Matches(classData);
            var features = (from Match mm in m select mm.Value).ToList().Select(mm => mm.Replace("add(new ", "").Replace("())", "")).ToList();
            features.Sort();
            return features;
        }

        private void SetIcon(Util.MeteorAddon addon, dynamic fabricJson)
        {
            if (fabricJson.icon != null)
            {
                addon.IconUrl =
                    $@"https://raw.githubusercontent.com/{addon.RepoMeta.Author}/{addon.RepoMeta.Name}/{addon.RepoMeta.DefaultBranch}/src/main/resources/{fabricJson.icon.ToString()}";
                Log("Got icon for addon!");
            } else Log("No icon for addon :(");
        }

        private static void SetDlMeta(Util.MeteorAddon addon, ReleaseAsset asset, Release latestRelease)
        {
            var dlMeta = addon.DownloadMeta;
            dlMeta.DownloadUrl = asset.BrowserDownloadUrl;
            dlMeta.DownloadCount = asset.DownloadCount;
            dlMeta.FileName = asset.Name;
            dlMeta.ReleaseUrl = latestRelease.HtmlUrl;
        }
        
        private static ReleaseAsset GetCorrectAsset(Release release)
        {
            return release.Assets.FirstOrDefault(a => !a.Name.Contains("-dev") && !a.Name.Contains("-sources") && !string.IsNullOrEmpty(a.BrowserDownloadUrl));
        }

        private static void SetAuthors(Util.MeteorAddon addon, dynamic fabricJson)
        {
            addon.Authors = new List<string>();
            if (fabricJson.authors != null) foreach (var author in fabricJson.authors) addon.Authors.Add(author.ToString());
            else addon.Authors.Add(addon.RepoMeta.Author);
        }

        private static bool IsOutdatedVer(string ver)
        {
            if (string.IsNullOrEmpty(ver)) return true;
            return !ver.Contains("1.19.2") && !ver.Contains("1.19.3");
        }

        private void SaveIcon(Util.MeteorAddon addon)
        {
            if (string.IsNullOrEmpty(addon.IconUrl)) return;
            Log($@"Saving compressed icon for {addon.Name}");
            var sw = new Stopwatch();
            sw.Start();
            using (var client = Util.GetWebClient())
            {
                byte[] data;
                try
                {
                    data = client.DownloadData(addon.IconUrl);
                }
                catch (WebException e)
                {
                    Log("Error saving icon");
                    Util.Log($@"WebException trying to save b64 icon for {addon.Name} : {e.Message}");
                    return;
                }
                using (var ms = new MemoryStream(data))
                {
                    var image = Image.FromStream(ms);
                    var resized = Util.ResizeImage(image, new Size(64, 64));
                    var imageB64 = Util.ImageToB64(resized, image.RawFormat); // save with the original format
                    addon.CompressedIcon = imageB64;
                }
            }
            sw.Stop();
            Log($@"Downloaded and compressed icon in {sw.ElapsedMilliseconds}ms");
        }
        
        private void Form1_Load(object sender, EventArgs e)
        { // make sure there's a github api key, and load the user-specified blacklists
            if (string.IsNullOrEmpty(Util.GetGithubKey())) NonBlockBox("Github api key file (github_key.txt) doesn't exist.\nYou will need a github api key to use the scraper.");
            Util.LoadBlacklists();
        }

        private static void NonBlockBox(string message)
        {
            var msg = new Thread(() =>
            {
                MessageBox.Show(message);
            });
            msg.Start();
        }
    }
}