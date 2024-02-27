using AhoCorasick.Net;
using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using XescSDK.Model;

namespace XescSDK
{
    public class DB
    {
        internal readonly HashSet<string> safeHashList = new HashSet<string>();
        internal readonly HashSet<string> malHashList = new HashSet<string>();
        internal readonly Dictionary<string, string> heurList = new Dictionary<string, string>();
        internal readonly AhoCorasickTree heurListPatterns;
        internal readonly Dictionary<string, string> heurScriptList = new Dictionary<string, string>();
        internal readonly AhoCorasickTree heurScriptListPatterns;
        internal readonly Dictionary<string, string> malVendorList = new Dictionary<string, string>();
        
        private readonly string databaseFolder;

        public DB(SettingsDTO settings)
        {
            databaseFolder = settings.DatabaseFolder;
            safeHashList = LoadList("whitelist.db", "dailywl.db");
            malHashList = LoadList("viruslist.db", "dailylist.db");
            heurList = LoadDictionary("heurlist.db", ',', true);
            heurListPatterns = LoadAhoCorasick("heurlist.db", ',');
            heurScriptList = LoadDictionary("heurlist2.db", ',', true);
            heurScriptListPatterns = LoadAhoCorasick("heurlist2.db", ',');
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

        private Dictionary<string, string> LoadDictionary(string path, char splitChar, bool replaceHifen = false)
        {
            var fullPath = Utils.RelativeToFullPath(databaseFolder, path);
            try
            {
                var file = File.ReadAllLines(fullPath);
                var dictionary = new Dictionary<string, string>(file.Length);
                foreach (var line in file)
                {
                    var splitLine = line.Split(splitChar);
                    var key = replaceHifen ? splitLine[0].Replace("-", "") : splitLine[0];
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

        private AhoCorasickTree LoadAhoCorasick(string path, char splitChar)
        {
            var fullPath = Utils.RelativeToFullPath(databaseFolder, path);
            try
            {
                var patterns = File.ReadAllLines(fullPath).Select(line => line.Split(splitChar)[0].Replace("-", "")).ToArray();
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
