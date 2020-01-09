using CsvHelper;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TFS.Search.Net
{
    class Program
    {
        private static string[] validSwitches = { "/F", "/T", "/P", "/O", "/R", "/S" };

        static void Main(string[] args)
        {
            try
            {
                if (args.Length < 1)
                {
                    PrintUsage();
                    return;
                }
                var argsNotSwitches = args.Where(a => a.Length <= 2 || (a.Length > 2 && validSwitches.Contains(a.Substring(0, 2)) == false));
                bool hasSearchInputFile = args.Any(a => a.Length > 2 && a.Substring(0, 2) == "/S");
                if (hasSearchInputFile == false && argsNotSwitches.Count() != 1)
                {
                    PrintUsage();
                    return;
                }
                else if (hasSearchInputFile == true && argsNotSwitches.Any())
                {
                    PrintUsage();
                    return;
                }

                string _filter = "*.*";                 // default
                string _tfsServer = "tfs-01";    // default
                string _tfsProject = string.Empty;
                string _outputFile = string.Empty;
                string _projectFileFilter = string.Empty;
                string _inputFileFilter = string.Empty;
                foreach (string sw in args)
                {
                    if (sw.StartsWith("/"))
                    {
                        if (sw.StartsWith("/F")) { _filter = sw.Replace("/F:", ""); }
                        if (sw.StartsWith("/T")) { _tfsServer = sw.Replace("/T:", ""); }
                        if (sw.StartsWith("/P")) { _tfsProject = sw.Replace("/P:", ""); }
                        if (sw.StartsWith("/O")) { _outputFile = sw.Replace("/O:", ""); }
                        if (sw.StartsWith("/R")) { _projectFileFilter = sw.Replace("/R:", ""); }
                        if (sw.StartsWith("/S")) { _inputFileFilter = sw.Replace("/S:", ""); }
                    }
                }
                List<string> _searchTerms = new List<string>();
                if (hasSearchInputFile)
                {
                    _searchTerms = GetSearchInput(_inputFileFilter);
                }
                else
                {
                    _searchTerms.Add(args[0]);
                }

                Console.WriteLine($"Saerching for '{string.Join(", ", _searchTerms)}' within {_tfsServer} (file type:{_filter})");
                Console.WriteLine($"{Environment.NewLine}****************************************{Environment.NewLine}");

                List<TfsMatchObj> matchList = new List<TfsMatchObj>();
                Uri tfsUri = new Uri($"http://{_tfsServer}:8080/tfs/DefaultCollection");

                var tfs = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(tfsUri);
                tfs.EnsureAuthenticated();

                var versionControlServer = tfs.GetService<VersionControlServer>();
                var allProjects = versionControlServer.GetAllTeamProjects(true);

                if (string.IsNullOrWhiteSpace(_tfsProject) == false)
                {
                    allProjects = allProjects.Where(p => p.Name == _tfsProject).ToArray();
                }
                else if (string.IsNullOrWhiteSpace(_projectFileFilter) == false)
                {
                    List<string> filterList = GetProjectFilterList(_projectFileFilter);
                    allProjects = allProjects.Where(p => filterList.Contains(p.Name)).ToArray();
                }

                Console.WriteLine($"Found {allProjects.Length} Projects...{Environment.NewLine}");
                foreach (var project in allProjects)
                {
                    Console.WriteLine($"Project {project.Name}:");
                    var allItemsInProject = versionControlServer.GetItems($"{project.ServerItem}/{_filter}", RecursionType.Full).Items;
                    var allFilesInProject = allItemsInProject.Where(i => i.ContentLength > 0 && i.ItemType == ItemType.File);

                    long position = 1;
                    long totalFileCount = allFilesInProject.Count();
                    Console.WriteLine($"checking {totalFileCount} files...:");
                    foreach (var fileItem in allFilesInProject)
                    {
                        Console.Write($"\r{position}/{totalFileCount}");
                        var workspace = fileItem.VersionControlServer.QueryWorkspaces(null, fileItem.VersionControlServer.AuthorizedUser, Environment.MachineName);
                        string localWorkspacePath = string.Empty;
                        if (workspace != null && workspace.FirstOrDefault() != null)
                        {
                            var filepath = workspace.First().Folders.FirstOrDefault();
                            if (filepath != null)
                            {
                                localWorkspacePath = filepath.LocalItem;
                            }
                        }
                        using (StreamReader reader = new StreamReader(fileItem.DownloadFile()))
                        {
                            int lineNum = 1;
                            string content = null;
                            do
                            {
                                content = reader.ReadLine();
                                if (string.IsNullOrEmpty(content)) { continue; }

                                foreach (string searchTerm in _searchTerms)
                                {
                                    int index = CultureInfo.CurrentCulture.CompareInfo.IndexOf(content, searchTerm, CompareOptions.IgnoreCase);
                                    if (index >= 0)
                                    {
                                        matchList.Add(new TfsMatchObj()
                                        {
                                            FileName = Path.GetFileName(fileItem.ServerItem),
                                            ServerPath = fileItem.ServerItem,
                                            LocalPath = string.IsNullOrWhiteSpace(localWorkspacePath) ? string.Empty : fileItem.ServerItem.Replace("$", localWorkspacePath).Replace("/", "\\"),
                                            MatchText = content,
                                            TfsProject = project.Name,
                                            CheckinDate = fileItem.CheckinDate,
                                            LineNumber = lineNum
                                        });
                                    }
                                }

                                lineNum++;
                            } while (content != null);

                        }
                        position++;
                    }
                    Console.WriteLine($"{Environment.NewLine}...found {matchList.Count()}...");
                }
                Console.WriteLine($"{Environment.NewLine}****************************************{Environment.NewLine}");

                foreach (TfsMatchObj m in matchList)
                {
                    Console.WriteLine(m.ServerPath);
                }

                if (string.IsNullOrWhiteSpace(_outputFile) == false)
                {
                    using (TextWriter writer = new StreamWriter(_outputFile))
                    {
                        using (CsvWriter csvWriter = new CsvWriter(writer))
                        {
                            csvWriter.WriteRecords(matchList);
                        }
                    }
                }
                Console.WriteLine("Completed.\t(Press any key to continue)");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine("\t(Press any key to continue)");
            }
            Console.ReadKey();
        }

        private static List<string> GetSearchInput(string inputFileFilter)
        {
            string[] allLines = File.ReadAllLines(inputFileFilter);
            return allLines.Where(l => string.IsNullOrWhiteSpace(l.Trim()) == false).ToList();
        }

        private static List<string> GetProjectFilterList(string projectFileFilter)
        {
            string[] allLines = File.ReadAllLines(projectFileFilter);
            return allLines.Where(l => string.IsNullOrWhiteSpace(l.Trim()) == false).ToList();
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage");
            Console.WriteLine("TFS.Search.Net.exe <search term (optional with /S)> <switches (optional)>");
            Console.WriteLine("");
            Console.WriteLine("\tSwitches: [all switches end with (:) colon]");
            Console.WriteLine("\t/S\tInput search file (filePath, newlines)");
            Console.WriteLine("\t\t\t - this will take the place of the <search term> parameter");
            Console.WriteLine("\t/F\tFile type Filter (*.cs)");
            Console.WriteLine("\t/T\tTFS Server");
            Console.WriteLine("\t/P\tTFS Project");
            Console.WriteLine("\t/R\tFilter by TFS Projects (filePath, newlines)");
            Console.WriteLine("\t/O\tOutput file (filePath for CSV)");
            Console.WriteLine("");
            Console.WriteLine("example: TFS.Search.Net.exe searchTerm /F:*.config");
            Console.WriteLine(@"example: TFS.Search.Net.exe searchTerm /F:*.cs /P:MyTFSProject /O:""C:\temp\test.csv""");
            Console.WriteLine(@"example: TFS.Search.Net.exe /S:""C:\mySearchTerms.txt"" /F:*.cs /R:""C:\MyProjectScope.txt"" /O:""C:\temp\test.csv""");
            Console.WriteLine("");
        }
    }

    public class TfsMatchObj
    {
        public string TfsProject { get; set; }
        public string FileName { get; set; }
        public string ServerPath { get; set; }
        public string LocalPath { get; set; }
        public DateTime CheckinDate { get; set; }
        public int LineNumber { get; set; }
        public string MatchText { get; set; }
    }
}

