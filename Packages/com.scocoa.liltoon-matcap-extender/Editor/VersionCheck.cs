using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Scocoa.MatCapExtender.Editor
{
    // Compares installed dependency versions with the versions listed in this package's "testedVersions".
    internal static class VersionCheck
    {
        public const string PackageName = "com.scocoa.liltoon-matcap-extender";

        [Serializable]
        class Manifest
        {
            public string version;
            public string changelogUrl;
        }

        static List<string> _warnings;
        static string _ownVersion;
        static string _url;

        public static string OwnVersion { get { Load(); return _ownVersion; } }
        public static string Url { get { Load(); return _url; } }

        public static IReadOnlyList<string> Warnings
        {
            get { Load(); return _warnings; }
        }

        public static void Reset() => _warnings = null;

        static void Load()
        {
            if (_warnings != null) return;
            _warnings = new List<string>();
            string manifestPath = "Packages/" + PackageName + "/package.json";
            string json = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : null;
            if (json == null)
            {
                _warnings.Add("package.json of " + PackageName + " not found.");
                return;
            }
            var mf = JsonUtility.FromJson<Manifest>(json);
            _ownVersion = mf.version;
            _url = mf.changelogUrl;

            foreach (var (pkg, tested) in ParseTested(json))
            {
                var info = PackageInfo.FindForAssetPath("Packages/" + pkg);
                string installed = info?.version;
                if (installed == null)
                    _warnings.Add($"{pkg} is not installed as a package (tested: {string.Join(", ", tested)}).");
                else if (!tested.Contains(installed))
                    _warnings.Add($"{pkg} {installed} has not been tested (tested: {string.Join(", ", tested)}). " +
                                  "Check the result, then update the tool or add the version to testedVersions.");
            }
        }

        // JsonUtility can't read dictionaries; "testedVersions" is a flat { "pkg": ["x.y.z", ...] } object.
        static IEnumerable<(string, List<string>)> ParseTested(string json)
        {
            int i = json.IndexOf("\"testedVersions\"", StringComparison.Ordinal);
            if (i < 0) yield break;
            int start = json.IndexOf('{', i), end = json.IndexOf('}', start);
            if (start < 0 || end < 0) yield break;
            string body = json.Substring(start + 1, end - start - 1);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(body, "\"([^\"]+)\"\\s*:\\s*\\[([^\\]]*)\\]"))
            {
                var list = new List<string>();
                foreach (System.Text.RegularExpressions.Match v in System.Text.RegularExpressions.Regex.Matches(m.Groups[2].Value, "\"([^\"]+)\""))
                    list.Add(v.Groups[1].Value);
                yield return (m.Groups[1].Value, list);
            }
        }
    }
}
