using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace KSPModDevInstaller
{
    internal class Program
    {
        private const string KSPPathEnvVar = "KSPDEVPATH";
        private const string GitBin = "git";
        private const string CKANBin = "ckan";
        
        private static readonly char DirSeparator = Path.DirectorySeparatorChar;

        public static string? KSPPath;
        public static string? RepoPath;

        private static void Main(string[] args)
        {
            Console.WriteLine();
            
            // get KSP path from env vars or by asking
            KSPPath = GetKSPPath();
            Console.WriteLine($"KSP dev install selected: {KSPPath}");
            KSPPath = KSPPath.TrimEnd(DirSeparator);
            
            Console.WriteLine();
            
            // get the repo path or clone it
            if (AskYesOrNo("Have you already cloned the repo?"))
            {
                RepoPath = AskRepoPath();
            }
            else
            {
                if (!AskRepoURLAndContinue(out string repoURL, out RepoPath))
                    return;
                Process gitProc = Process.Start(GitBin, $"clone {repoURL} \"{RepoPath}\"");
                gitProc.WaitForExit();
                gitProc.Dispose();
            }
            
            Console.WriteLine();

            // find the netkan and optionally install the mod through CKAN
            string[] modNetkans = Directory.GetFiles(RepoPath, "*.netkan", SearchOption.AllDirectories);
            foreach (string netkan in modNetkans)
            {
                Console.WriteLine($"Found netkan: {Path.GetFileName(netkan)}");
                ParseNetkanAndInstallMod(netkan);
            }

            if (modNetkans.Length == 0)
            {
                Console.WriteLine("No .netkan files found in the repo.");
                FallbackCKANInstall("Do you wish to install a mod through CKAN anyway?");
            }
            
            Console.WriteLine();

            // find the gamedata in the repo and optionally symlink its contents
            string[] repoGamedatas = Directory.GetDirectories(RepoPath, "GameData", SearchOption.AllDirectories);
            if (repoGamedatas.Length > 0)
            {
                string[] repoModDirs = Directory.GetDirectories(repoGamedatas[0]);
                GameDataSymlink(repoModDirs); //realistically, there's only 1 gamedata
            }
            
            Console.WriteLine();

            // look for .csproj files and create .csproj.users with the dependencies
            string[] csprojs = Directory.GetFiles(RepoPath, "*.csproj", SearchOption.AllDirectories);
            if (csprojs.Length <= 0)
            {
                Console.WriteLine("No .csproj files found in the repo.");
                return;
            }
            Console.WriteLine($"Found {csprojs.Length} .csproj file(s) in the mod repo.");
            if (AskYesOrNo("Do you want to add corresponding .csproj.user files referencing dependencies to your install?"))
            {
                foreach (string csproj in csprojs)
                {
                    Console.WriteLine($"Creating {csproj}.user");
                    string csprojUser = csproj + ".user";
                    
                    CreateCsprojUserFile(csproj, csprojUser);
                }
            }
        }

        private static bool AskYesOrNo(string message, bool defaultResponse = true)
        {
            bool? answer = null;

            do
            {
                Console.Write(message + (defaultResponse ? " [Y/n]: " : " [y/N]: "));
                string? answerLine = Console.ReadLine();

                if (!string.IsNullOrEmpty(answerLine))
                {
                    if (string.Compare(answerLine, 0, "Y", 0, 1, ignoreCase: true) == 0)
                        answer = true;
                    else if (string.Compare(answerLine, 0, "N", 0, 1, ignoreCase: true) == 0)
                        answer = false;
                    else
                        Console.WriteLine("Invalid response.");
                }
                else
                {
                    answer = defaultResponse;
                }
            } while (!answer.HasValue);
            
            return answer.Value;
        }

        private static string GetKSPPath()
        {
            string? kspPath = Environment.GetEnvironmentVariable(KSPPathEnvVar);
            
            if (string.IsNullOrEmpty(kspPath))
            {
                Console.WriteLine($"Environment variable {KSPPathEnvVar} not found. Input the path of your KSP install:");
                kspPath = Console.ReadLine();
            }
            else if (!Directory.Exists(kspPath))
            {
                Console.WriteLine($"The {KSPPathEnvVar} env variable was found, but it doesn't point to a valid directory.");
                Console.WriteLine("Input a valid path for your KSP install:");
                kspPath = Console.ReadLine();
            }
            
            // if the directory isn't valid, nag the user until it is
            while (!Directory.Exists(kspPath))
            {
                Console.WriteLine("Invalid Path. Input a valid path for your KSP install:");
                kspPath = Console.ReadLine();
            }

            return Path.GetFullPath(kspPath);
        }
        
        private static string AskRepoPath()
        {
            Console.Write("Enter git repository path: ");
            string? ans = Console.ReadLine();

            while (!Directory.Exists($"{ans}{DirSeparator}.git{DirSeparator}"))
            {
                Console.WriteLine("Path does not exist or does not point to a valid git repo.");
                Console.Write("Enter git repository path: ");
                ans = Console.ReadLine();
            }

            return Path.GetFullPath(ans!);
        }
        
        private static bool AskRepoURLAndContinue(out string repoURL, out string repoPath)
        {
            var reg = new Regex(@"((git|ssh|https?)|(git@[\w\.]+)):(//)?([\w\.@/~:-]+)/([\w\.@~:-]+)(\.git)?\/?");
            
            Console.Write("Enter the mod repo url: ");
            string? answer = Console.ReadLine();

            while (string.IsNullOrEmpty(answer) || !reg.Match(answer).Success)
            {
                Console.WriteLine("Invalid URL.");
                Console.Write("Enter the mod repo url: ");
                answer = Console.ReadLine();
            }

            // if the match is successful, the sixth group should be the repo name and the first the link
            var repoName = reg.Match(answer).Groups[6].ToString();
            repoURL = reg.Match(answer).Groups[0].ToString();
            
            // make git clone the repo next to the running program
            string? pwd = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            repoPath = $"{pwd}{DirSeparator}{repoName}";
            
            return AskYesOrNo($"Repo {repoName} found. It will be cloned at {repoPath}. Continue? ");
        }

        private static void ParseNetkanAndInstallMod(string netkan)
        {
            string json = File.ReadAllText(netkan);
            string? modId;
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                modId = document.RootElement.GetProperty("identifier").GetString();
            }

            if (!string.IsNullOrEmpty(modId))
            {
                Console.WriteLine($"Do you want to install mod {modId} and its dependencies through CKAN?");
                if (AskYesOrNo("You can decline if you have already installed it."))
                {
                    InstallModWithCKAN(modId);
                }
            }
            else
            {
                Console.WriteLine($"No identifier found in {netkan}.");
                FallbackCKANInstall("Do you wish to install a mod through CKAN anyway?");
            }
        }

        private static void InstallModWithCKAN(string modId)
        {
            using var ckanProc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = CKANBin,
                    Arguments = $"install --no-recommends --gamedir \"{KSPPath}\" \"{modId}\"",
                    UseShellExecute = false,
                }
            };
            ckanProc.Start();
            ckanProc.WaitForExit();
        }

        private static void GameDataSymlink(string[] repoModDirs)
        {
            if (repoModDirs.Length <= 0)
            {
                Console.WriteLine("No GameData found in the repo. Cannot symlink.");
                return;
            }
            
            Console.WriteLine($"GameData found in the repo: found folder(s):");
            foreach (string modDir in repoModDirs)
                Console.WriteLine(modDir);
            Console.WriteLine(
                "NOTE: symlinking will delete and replace the old mod folder in your KSP install with a link to the same folder in the repo.");
            Console.WriteLine("Answer yes only if you are sure there are no changes in your KSP GameData that you can't loose.");

            foreach (string repoModDir in repoModDirs)
            {
                string modDirName = new DirectoryInfo(repoModDir).Name; //equivalent to basename(dir)
                
                if (!AskYesOrNo($"Do you want to symlink GameData{DirSeparator}{modDirName} to your install?"))
                    continue;

                string kspModDir = $"{KSPPath}{DirSeparator}GameData{DirSeparator}{modDirName}";
                
                try
                {
                    if (Directory.Exists(kspModDir))
                    {
                        Directory.Delete(kspModDir, true);
                        // Directory.Delete() is not instantaneous, and symlinking can fail due to the OS
                        // not having deleted the directory yet:   https://stackoverflow.com/a/25421332
                        // we wait until we know it doesn't exist anymore to continue
                        while (Directory.Exists(kspModDir)) Thread.Sleep(1);
                    }
                    // also check for file because a symlink is a file, at least on linux
                    if (File.Exists(kspModDir))
                    {
                        File.Delete(kspModDir);
                        while (File.Exists(kspModDir)) Thread.Sleep(1);
                    }
                    // creates a symlink at `path`, pointing to `pathToTarget`
                    Directory.CreateSymbolicLink(kspModDir, repoModDir);
                    
                    Console.WriteLine($"Symlink created from {repoModDir} to {kspModDir}");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        private static void CreateCsprojUserFile(string csprojFile, string csprojUserFile)
        {
            var dllReferences = GetCsprojReferences(csprojFile);
            
            string gamedataPath = $"{KSPPath}{DirSeparator}GameData";
            var dllPathDict = new Dictionary<string, string?>(); // dict ensures that all paths are unique (as long as the dlls are)

            foreach (string dll in dllReferences)
            {
                var files = Directory.GetFiles(gamedataPath, $"{dll}.dll", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    // get the first option, hopefully there's only one
                    dllPathDict[dll] = Path.GetDirectoryName(files[0]);
                }
            }
            
            WriteCsprojUserFile(csprojUserFile, dllPathDict.Values.ToList());
        }

        private static IEnumerable<string> GetCsprojReferences(string csprojFile)
        {
            var csprojXml = new XmlDocument();
            csprojXml.Load(csprojFile);

            var refDlls = new List<string>();
            
            var mgr = new XmlNamespaceManager(csprojXml.NameTable);
            mgr.AddNamespace("x", "http://schemas.microsoft.com/developer/msbuild/2003");
            XmlNodeList? refNodes = csprojXml.SelectNodes("//x:Reference", mgr);

            if (refNodes == null || refNodes.Count <= 0)
            {
                Console.WriteLine("No references found.");
                return refDlls;
            }
            
            foreach (XmlElement item in refNodes)
            {
                string dllName = item.GetAttribute("Include").Split(',').First();
                refDlls.Add(dllName);
            }

            return refDlls;
        }
        
        private static void WriteCsprojUserFile(string csprojUserFile, List<string?> refPaths)
        {
            var csprojUserXml = new XmlDocument();

            // <?xml version="1.0" encoding="utf-8"?>
            var xmlDec = csprojUserXml.CreateXmlDeclaration("1.0", "UTF-8", null);
            var root = csprojUserXml.DocumentElement;
            csprojUserXml.InsertBefore(xmlDec, root);
            
            // <Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
            XmlElement projElem = csprojUserXml.CreateElement("", "Project", null);
            projElem.SetAttribute("ToolsVersion", "Current");
            projElem.SetAttribute("xmlns", "http://schemas.microsoft.com/developer/msbuild/2003");
            csprojUserXml.AppendChild(projElem);

            // <PropertyGroup>
            XmlElement propGrpElem = csprojUserXml.CreateElement("", "PropertyGroup", null);
            projElem.AppendChild(propGrpElem);
            
            // ReferencePath element with KSP_Data/Managed in it
            XmlElement refPathDataManagedElem = csprojUserXml.CreateElement("", "ReferencePath", null);
            if (Directory.Exists($"{KSPPath}{DirSeparator}KSP_x64_Data"))
            {
                XmlText nodeText =
                    csprojUserXml.CreateTextNode($"{KSPPath}{DirSeparator}KSP_x64_Data{DirSeparator}Managed");
                refPathDataManagedElem.AppendChild(nodeText);
            }
            else //Linux has no KSP_x64_Data directory
            {
                XmlText nodeText =
                    csprojUserXml.CreateTextNode($"{KSPPath}{DirSeparator}KSP_Data{DirSeparator}Managed");
                refPathDataManagedElem.AppendChild(nodeText);
            }
            propGrpElem.AppendChild(refPathDataManagedElem);
            
            foreach (string? path in refPaths)
            {
                // ReferencePath element with the paths extracted from GameData
                XmlElement refPathElem = csprojUserXml.CreateElement("", "ReferencePath", null);
                refPathElem.AppendChild(csprojUserXml.CreateTextNode(path));
                propGrpElem.AppendChild(refPathElem);
            }

            csprojUserXml.Save(csprojUserFile);
        }

        private static void FallbackCKANInstall(string RequestMessage)
        {
            if (!AskYesOrNo(RequestMessage)) return;
            
            Console.WriteLine("Input the identifier of the mod(s): ");
            string? ans = Console.ReadLine();

            while (string.IsNullOrEmpty(ans))
            {
                Console.WriteLine("Invalid answer. Input the identifier of the mod(s): ");
                ans = Console.ReadLine();
            }
                
            InstallModWithCKAN(ans);
        }
    }
}