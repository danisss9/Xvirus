using AhoCorasick.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xvirus.Model;

namespace Xvirus
{
    public class DB
    {
        internal readonly HashSet<string> safeHashList = new();
        internal readonly HashSet<string> malHashList = new();
        internal readonly Dictionary<string, string> heurList = new();
        internal readonly Dictionary<string, string[]> heurListDeps = new();
        internal readonly AhoCorasickTree heurListPatterns;
        internal readonly Dictionary<string, string> heurScriptList = new();
        internal readonly Dictionary<string, string[]> heurScriptListDeps = new();
        internal readonly AhoCorasickTree heurScriptListPatterns;
        internal readonly Dictionary<string, string> malVendorList = new();
        
        private readonly string databaseFolder;

        public DB(SettingsDTO settings)
        {
            databaseFolder = settings.DatabaseFolder;
            safeHashList = LoadList("whitelist.db", "dailywl.db");
            malHashList = LoadList("viruslist.db", "dailylist.db");
            (heurList, heurListDeps) = LoadDictionary("heurlist.db", ',', '&');
            heurListPatterns = LoadAhoCorasick("heurlist.db", ',', '&');
            (heurScriptList, heurScriptListDeps) = LoadDictionary("heurlist2.db", ',', '&');
            heurScriptListPatterns = LoadAhoCorasick("heurlist2.db", ',', '&');
            malVendorList = LoadDictionary("malvendor.db", '|');
        }

        private HashSet<string> LoadList(params string[] paths)
        {
            var list = new HashSet<string>();
            foreach (var path in paths)
            {
                var fullPath = Utils.RelativeToFullPath(databaseFolder, path);
                try
                {
                    var file = File.ReadAllLines(fullPath);
                    list.UnionWith(file);
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex);
                    throw;
                }
            }
            return list;
        }

        private Dictionary<string, string> LoadDictionary(string path, char splitChar)
        {
            var fullPath = Utils.RelativeToFullPath(databaseFolder, path);
            try
            {
                var file = File.ReadAllLines(fullPath);
                var dictionary = new Dictionary<string, string>(file.Length);
                foreach (var line in file)
                {
                    var splitLine = line.Split(splitChar);
                    var key = splitLine[0];
                    if (!dictionary.ContainsKey(key))
                    {
                        dictionary.Add(key, splitLine[1]);
                    }
                }
                return dictionary;
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                throw;
            }
        }

        private (Dictionary<string, string>, Dictionary<string, string[]>) LoadDictionary(string path, char splitChar, char andChar)
        {
            var fullPath = Utils.RelativeToFullPath(databaseFolder, path);
            try
            {
                var file = File.ReadAllLines(fullPath);
                var dictionary = new Dictionary<string, string>(file.Length);
                var dictionary2 = new Dictionary<string, string[]>();
                foreach (var line in file)
                {
                    var splitLine = line.Split(splitChar);
                    var key = splitLine[0].Replace("-", "", StringComparison.Ordinal);

                    if (!dictionary.ContainsKey(key))
                    {
                        dictionary.Add(key, splitLine[1]);

                        if (splitLine.Length > 2)
                        {
                            dictionary2.Add(key, splitLine[2].Split(andChar));
                        }
                    }
                }
                return (dictionary, dictionary2);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                throw;
            }
        }

        private AhoCorasickTree LoadAhoCorasick(string path, char splitChar, char andChar)
        {
            var fullPath = Utils.RelativeToFullPath(databaseFolder, path);
            try
            {
                var patterns = File.ReadAllLines(fullPath)
                    .Select(line => line.Split(splitChar))
                    .SelectMany(splitLines => splitLines.Length > 2 ? splitLines.Take(1).Concat(splitLines[2].Split(andChar)) : splitLines.Take(1))
                    .Select(line => line.Replace("-", "", StringComparison.Ordinal))
                    .ToArray();
                return new AhoCorasickTree(patterns);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
                throw;
            }
        }
    }
}
