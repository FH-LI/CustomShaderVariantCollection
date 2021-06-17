using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine.Rendering;

public class ShaderVariantCollectionWindow : EditorWindow
{
    static ShaderVariantCollectionWindow _window;
    static string svcPath;
    static string shaderFolderPath;
    static string packageFolderPath;
    string errorTips;
    string tempStr;

    [MenuItem("Tools/Art/ShaderVariantCollection")]
    public static void OpenWindow()
    {
        _window = (ShaderVariantCollectionWindow)GetWindow(typeof(ShaderVariantCollectionWindow), false, "ShaderVariantCollection", true);
        _window.Show();
    }

    private void OnGUI()
    {
        svcPath = EditorGUI.TextField(new Rect(5, 15, position.width - 60, 20), "SVCFilePath", svcPath);
        if (GUI.Button(new Rect(position.width - 50, 15, 20, 20), "..."))
        {
            svcPath = EditorUtility.OpenFilePanelWithFilters("SVCFilePath", svcPath, new string[] { "SVC", "shadervariants" });
            if (!string.IsNullOrEmpty(svcPath))
            {
                int index = svcPath.IndexOf("Assets");
                svcPath = svcPath.Substring(index);
            }
        }

        shaderFolderPath = EditorGUI.TextField(new Rect(5, 50, position.width - 60, 20), "ShaderFolderPath", shaderFolderPath);
        if (GUI.Button(new Rect(position.width - 50, 50, 20, 20), "..."))
         {
            tempStr = EditorUtility.OpenFolderPanel("ShaderFolderPath", shaderFolderPath, "");
            if (!string.IsNullOrEmpty(tempStr))
            {
                shaderFolderPath = tempStr;
                tempStr = null;
            }
        }

        packageFolderPath = EditorGUI.TextField(new Rect(5, 85, position.width - 60, 20), "PackageShaderFolderPath", packageFolderPath);
        if (GUI.Button(new Rect(position.width - 50, 85, 20, 20), "..."))
        {
            tempStr = EditorUtility.OpenFolderPanel("PackageShaderFolderPath", packageFolderPath, "");
            if (!string.IsNullOrEmpty(tempStr))
            {
                packageFolderPath = tempStr;
                tempStr = null;
            }
        }

        if (GUI.Button(new Rect(5, 120, 100, 30), "StartCollection"))
        {
            if (Directory.Exists(shaderFolderPath) && (string.IsNullOrEmpty(packageFolderPath) || Directory.Exists(packageFolderPath)))
            {
                errorTips = "";
                StartShaderVariantCollection();
            }
            else
                errorTips = "FolderPathError";
        }
        EditorGUI.LabelField(new Rect(5, 155, position.width - 60, 30), errorTips);
    }

    private static void GetDirectoryList(string filePath, List<DirectoryInfo> directoryList)
    {
        if (!Directory.Exists(filePath)) return;
        DirectoryInfo thisDirectory = new DirectoryInfo(filePath);
        directoryList.Add(thisDirectory);
        foreach (DirectoryInfo directory in thisDirectory.GetDirectories())
        {
            GetDirectoryList(directory.FullName, directoryList);
        }
    }

    public static List<FileInfo> GetFileList(string filePath, params string[] pattern)
    {
        List<FileInfo> fileList = new List<FileInfo>();
        if (!Directory.Exists(filePath)) return fileList;
        List<DirectoryInfo> directoryList = new List<DirectoryInfo>();
        GetDirectoryList(filePath, directoryList);
        foreach (DirectoryInfo directory in directoryList)
        {
            foreach (FileInfo file in directory.GetFiles())
            {
                if (pattern != null && pattern.Length > 0)
                {
                    string suffix = System.Text.RegularExpressions.Regex.Match(file.FullName, @"\.[a-zA-Z0-9-]*$").Value;
                    if (string.IsNullOrEmpty(suffix)) continue;
                    suffix = suffix.ToLower();
                    bool flag = Array.IndexOf(pattern, suffix) != -1;
                    if (flag)
                        fileList.Add(file);
                }
                else
                {
                    fileList.Add(file);
                }
            }
        }
        return fileList;
    }

    public static void StartShaderVariantCollection()
    {
        List<FileInfo> shaderFolderFileInfos = GetFileList(shaderFolderPath, ".shader");
        List<FileInfo> packageShaderFolderFileInfos = GetFileList(packageFolderPath, ".shader");
        int index = Application.dataPath.IndexOf("Assets");

        MethodInfo variantCountMethod = typeof(ShaderUtil).GetMethod("GetVariantCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        List<Shader> shaderList = new List<Shader>();
        Dictionary<Shader, ShaderVariantData> shaderVariantDatas = new Dictionary<Shader, ShaderVariantData>();
        ShaderVariantCollection newSvc = new ShaderVariantCollection();
        Dictionary<Shader, List<ShaderKeyPass>> kpDict = new Dictionary<Shader, List<ShaderKeyPass>>();

        foreach (FileInfo file in shaderFolderFileInfos)
        {
            string path = file.FullName.Substring(index);
            Shader s = AssetDatabase.LoadAssetAtPath<Shader>(path);

            var variantCount = variantCountMethod.Invoke(null, new System.Object[] { s, true });
            int _variantCount = int.Parse(variantCount.ToString());

            if (s == null || _variantCount == 0) continue;
            if (shaderList.Contains(s)) continue;

            shaderList.Add(s);
            newSvc.Add(new ShaderVariantCollection.ShaderVariant()
            {
                shader = s
            });
        }

        //收集SHADER里的 KEYWORDS
        foreach (Shader s in shaderList)
        {
            List<string> SelectedKeywords = new List<string>();
            var svd = GetShaderEntriesData(s, newSvc, ref SelectedKeywords, 65536);
            svd.shader = s;
            shaderVariantDatas.Add(s, svd);
            SaveShaderPassAndKeyword(ref kpDict, svd);
        }

        var matDict = CollectMaterial(shaderList);
        Dictionary<Shader, List<string[]>> addFeatureKeywordDict = new Dictionary<Shader, List<string[]>>();
        foreach (Shader s in matDict.Keys)
        {
            //Keep only the KEYWORDS of multi, and clean up the keywords other than the material's redundant feature shader
            List<string[]> featureKey = ShaderAnalysis(shaderVariantDatas[s], matDict[s]);
            addFeatureKeywordDict.Add(s, featureKey);
        }

        if (!File.Exists(svcPath))
        {
            AssetDatabase.CreateAsset(newSvc, svcPath);
        }
        newSvc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(svcPath);
        newSvc.Clear();

        List<string> finals = new List<string>();
        foreach (var pairdata in shaderVariantDatas)
        {
            var data = pairdata.Value;
            List<string[]> featureKeyList = null;
            //KEY of MULTI_COMPLIE to combine SHADER_FEATURE KEYS of material instance
            for (int j = 0; j < data.keywordLists.Length; j++)
            {
                if (addFeatureKeywordDict.TryGetValue(data.shader, out featureKeyList))
                {
                    if (featureKeyList == null)
                        continue;

                    foreach (string[] sss in featureKeyList)
                    {
                        finals.Clear();
                        string[] keyArr = data.keywordLists[j].Trim().Split(' ');
                        finals.AddRange(keyArr);
                        finals.AddRange(sss);

                        int passType = 0;
                        if (kpDict.ContainsKey(data.shader))
                            passType = FindShaderVariantPass(kpDict[data.shader], finals.ToArray());

                        newSvc.Add(new ShaderVariantCollection.ShaderVariant()
                        {
                            shader = data.shader,
                            passType = (PassType)passType,
                            keywords = finals.ToArray()
                        });
                    }
                }

                featureKeyList = null;
                //Add the KEY when only SHADER_FEATURE
                if (addFeatureKeywordDict.TryGetValue(data.shader, out featureKeyList))
                {
                    if (featureKeyList != null)
                    {
                        foreach (string[] sss in featureKeyList)
                        {
                            finals.Clear();
                            finals.AddRange(sss);

                            int passType = 0;
                            if (kpDict.ContainsKey(data.shader))
                                passType = FindShaderVariantPass(kpDict[data.shader], finals.ToArray());

                            newSvc.Add(new ShaderVariantCollection.ShaderVariant()
                            {
                                shader = data.shader,
                                passType = (PassType)passType,
                                keywords = finals.ToArray()
                            });
                        }
                    }
                }
            }
        }

        //PACKAGE only adds SHADER without variants
        HashSet<Shader> shaders = new HashSet<Shader>();
        UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(packageFolderPath);
        if (obj != null)
        {
            EditorGUIUtility.PingObject(obj);
            Selection.activeObject = obj;
            UnityEngine.Object[] selectShader = Selection.GetFiltered(typeof(Shader), SelectionMode.DeepAssets);
            foreach (Shader s in selectShader)
            {
                if (!shaders.Contains(s))
                {
                    newSvc.Add(new ShaderVariantCollection.ShaderVariant
                    {
                        shader = s
                    });
                    shaders.Add(s);
                }
            }
        }
        EditorUtility.SetDirty(newSvc);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static Dictionary<Shader, List<Material>> CollectMaterial(List<Shader> shaderList)
    {
        Dictionary<Shader, List<Material>> dict = new Dictionary<Shader, List<Material>>();
        foreach (Shader s in shaderList)
        {
            dict.Add(s, new List<Material>());
        }

        string[] matGuid = AssetDatabase.FindAssets("t:material");
        foreach (string guid in matGuid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;
            if (dict.ContainsKey(mat.shader))
            {
                dict[mat.shader].Add(mat);
            }
        }

        return dict;
    }

    static bool ContainStringArrays(List<string[]> lsss, string[] arr)
    {
        bool re = false;
        foreach (string[] sss in lsss)
        {
            int count1 = sss.Intersect(arr).Count();
            if (count1 == arr.Count() && count1 == sss.Count())
            {
                return true;
            }
        }

        return re;
    }

    static List<string> tkeyArr = new List<string>();

    public static List<string[]> ShaderAnalysis(ShaderVariantData shaderdata, List<Material> matList)
    {
        string[] listAll = shaderdata.keywordLists;
        List<string> onlyMulties = new List<string>();

        System.Text.RegularExpressions.Regex REG_SHADER_FEATURE_KEYWORDS = new System.Text.RegularExpressions.Regex(@"^Keywords stripped away when not used: (.+)$");
        System.Text.RegularExpressions.Match match;
        var combFilePath = String.Format("{0}/ParsedCombinations-{1}.shader", GetProjectUnityTempPath(), shaderdata.shader.name.Replace('/', '-'));

        if (File.Exists(combFilePath))
        {
            File.Delete(combFilePath);
        }

        //打开编译好的shader
        MethodInfo method = typeof(ShaderUtil).GetMethod("OpenShaderCombinations", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        method.Invoke(null, new object[] { shaderdata.shader, true });

        tkeyArr.Clear();
        if (File.Exists(combFilePath))
        {
            var lines = File.ReadAllLines(combFilePath);
            for (int i = 0; i < lines.Length; i++)
            {
                //Determine whether shader feature, if yes, save
                string line = lines[i];
                if ((match = REG_SHADER_FEATURE_KEYWORDS.Match(line)).Success)
                {
                    string key = match.Groups[1].Value;
                    if (string.IsNullOrEmpty(key)) continue;
                    tkeyArr.AddRange(key.Trim().Split(' '));
                }
            }
        }

        foreach (string keywords in listAll)
        {
            bool isPureMulties = true;
            foreach (string s in tkeyArr)
            {
                if (keywords.Contains(s))
                {
                    isPureMulties = false;
                    break;
                }
            }

            if (isPureMulties && !onlyMulties.Contains(keywords))
            {
                onlyMulties.Add(keywords);
            }
        }

        shaderdata.keywordLists = onlyMulties.ToArray();

        List<string[]> shaderFeatureKeyword = new List<string[]>();
        List<string> keywordHash = new List<string>();

        //Filter redundant shader_feature
        foreach (Material mat in matList)
        {
            keywordHash.Clear();

            string[] matKeywords = mat.shaderKeywords;
            foreach (string keyword in matKeywords)
            {
                if (shaderdata.remainingKeywords.Contains(keyword) && tkeyArr.Contains(keyword))
                {
                    keywordHash.Add(keyword);
                }
            }

            string[] nsss = keywordHash.ToArray();
            if (!ContainStringArrays(shaderFeatureKeyword, nsss))
                shaderFeatureKeyword.Add(nsss);
        }

        return shaderFeatureKeyword;
    }

    public static String GetProjectUnityTempPath()
    {
        var rootPath = Environment.CurrentDirectory.Replace('\\', '/');
        rootPath += "/Temp";
        if (Directory.Exists(rootPath))
        {
            rootPath = Path.GetFullPath(rootPath);
            return rootPath.Replace('\\', '/');
        }
        else
        {
            return rootPath;
        }
    }

    public static void SaveShaderPassAndKeyword(ref Dictionary<Shader, List<ShaderKeyPass>> kpDict, ShaderVariantData svData)
    {
        if (kpDict == null)
            kpDict = new Dictionary<Shader, List<ShaderKeyPass>>();
        for (int i = 0, length = svData.passTypes.Length; i < length; i++)
        {
            if (!kpDict.ContainsKey(svData.shader))
                kpDict.Add(svData.shader, new List<ShaderKeyPass>());
            else if (kpDict[svData.shader] == null)
                kpDict[svData.shader] = new List<ShaderKeyPass>();

            List<ShaderKeyPass> kpList = kpDict[svData.shader];
            string keyword = svData.keywordLists[i];
            string sortKeyword = SortShaderVarantKeyword(keyword);
            if ((string.IsNullOrEmpty(sortKeyword)) || kpList.Find(item => item.keywordLists.Equals(sortKeyword)) == null)
            {
                kpList.Add(new ShaderKeyPass()
                {
                    keywordLists = sortKeyword,
                    passTypes = svData.passTypes[i]
                });
            }
        }
    }

    public static string SortShaderVarantKeyword(string[] keywordList, char Separator = ' ')
    {
        Array.Sort(keywordList, (a, b) =>
        {
            if (a.Length < b.Length) return -1;
            else
                return a.CompareTo(b);
        });
        StringBuilder sb = new StringBuilder();
        Array.ForEach(keywordList, item => sb.Append(item).Append(" "));
        string result = sb.ToString().Trim();
        return result;
    }

    public static string SortShaderVarantKeyword(string keyword, char Separator = ' ')
    {
        string[] keywordList = keyword.Trim().Split(Separator);
        if (keywordList.Length == 1) return keyword;
        return SortShaderVarantKeyword(keywordList, Separator);
    }

    public static int FindShaderVariantPass(List<ShaderKeyPass> kpList, string[] keyword)
    {
        string sortPassword = SortShaderVarantKeyword(keyword);
        ShaderKeyPass kpData = null;
        int index = kpList.FindIndex(item => item.keywordLists.Equals(sortPassword));
        if (index != -1)
        {
            kpData = kpList[index];
            kpList.RemoveAt(index);
        }
        return kpData?.passTypes ?? 0;
    }

    public class ShaderKeyPass
    {
        public int passTypes;
        public string keywordLists;
    }

    public class ShaderVariantData
    {
        public Shader shader;
        public int[] passTypes;
        public string[] keywordLists;
        public string[] remainingKeywords;
    }

    public static ShaderVariantData GetShaderEntriesData(Shader sd, ShaderVariantCollection svc, ref List<string> SelectedKeywords, int maxEntries = 256)
    {
        string[] keywordLists = null, remainingKeywords = null;
        int[] FilteredVariantTypes = null;
        MethodInfo GetShaderVariantEntries = typeof(ShaderUtil).GetMethod("GetShaderVariantEntriesFiltered", BindingFlags.NonPublic | BindingFlags.Static);
        object[] args = new object[] {
            sd,
            maxEntries,
            SelectedKeywords.ToArray (),
            svc,
            FilteredVariantTypes,
            keywordLists,
            remainingKeywords
        };
        GetShaderVariantEntries.Invoke(null, args);
        ShaderVariantData svd = new ShaderVariantData();
        svd.passTypes = args[4] as int[];
        svd.keywordLists = args[5] as string[];
        svd.remainingKeywords = args[6] as string[];
        return svd;
    }
}
