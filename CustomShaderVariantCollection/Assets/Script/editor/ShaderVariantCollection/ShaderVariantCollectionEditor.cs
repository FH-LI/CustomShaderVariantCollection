using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(ShaderVariantCollection))]
public class ShaderVariantCollectionEditor : Editor
{

    public struct ShaderVariantInfo
    {
        public Shader shader;
        public string path;
        public int variantCount;
        public List<string> keyword;
        public List<int> pass;
    }

    List<bool> toggleStateList;
    List<ShaderVariantInfo> shaderVariants;
    int totalVariantCount = 0;
    string input = null;
    string regexRuler = ".*";
    List<ShaderVariantInfo> matchShaderVariant;
    bool isSortByCount = false;
    string buttonStr = "SortByVariantCount";
    public void CalcuShaderVariant()
    {
        shaderVariants = new List<ShaderVariantInfo>();
        var so = serializedObject;
        SerializedProperty shaderMap = so.FindProperty("m_Shaders");
        matchShaderVariant = new List<ShaderVariantInfo>();
        for (int i = 0, size = shaderMap.arraySize; i < size; i++)
        {
            Shader s = (Shader)shaderMap.GetArrayElementAtIndex(i).FindPropertyRelative("first").objectReferenceValue;
            var property = shaderMap.GetArrayElementAtIndex(i).FindPropertyRelative("second.variants");
            List<string> keywordList = new List<string>();
            List<int> passtypeList = new List<int>();
            for (int keywordIndex = 0; keywordIndex < property.arraySize; keywordIndex++)
            {
                string keyword = property.GetArrayElementAtIndex(keywordIndex).FindPropertyRelative("keywords").stringValue;
                keywordList.Add(keyword);
                int passtype = property.GetArrayElementAtIndex(keywordIndex).FindPropertyRelative("passType").intValue;
                passtypeList.Add(passtype);
            }
            string path = AssetDatabase.GetAssetPath(s);
            shaderVariants.Add(new ShaderVariantInfo
            {
                shader = s,
                path = path,
                variantCount = property.arraySize,
                keyword = keywordList,
                pass = passtypeList,
            });
            if (Regex.IsMatch(shaderVariants[i].shader.name, regexRuler, RegexOptions.IgnoreCase))
            {
                matchShaderVariant.Add(shaderVariants[i]);
            }
        }
        if (isSortByCount)
            matchShaderVariant.Sort((a, b) => b.variantCount - a.variantCount);
        else
            matchShaderVariant.Sort((a, b) => a.path.CompareTo(b.path));
        //shaderVariants.Sort((a, b) => (a.variantCount - b.variantCount) * -1);
        totalVariantCount = 0;
        shaderVariants.ForEach(item => totalVariantCount += item.variantCount);
        buttonStr = isSortByCount ? "SortByShaderName" : "SortByVariantCount";
    }

    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Refresh"))
        {
            CalcuShaderVariant();
        }
        if (GUILayout.Button(buttonStr))
        {
            isSortByCount = !isSortByCount;
            buttonStr = isSortByCount ? "SortByShaderName" : "SortByVariantCount";
            if (isSortByCount)
                matchShaderVariant.Sort((a, b) => b.variantCount - a.variantCount);
            else
                matchShaderVariant.Sort((a, b) => a.path.CompareTo(b.path));
        }
        if (shaderVariants == null || shaderVariants.Count == 0)
            CalcuShaderVariant();
        GUILayout.Label(string.Format("TotalVariantCount:{0}", totalVariantCount));
        GUILayout.Label(string.Format("TotalShaderCount:{0}", shaderVariants.Count));
        GUILayout.BeginHorizontal();
        GUILayout.Label("按shader名字正则", GUILayout.MaxWidth(200));
        EditorGUI.BeginChangeCheck();
        input = GUILayout.TextField(input, GUILayout.MaxWidth(350));
        if (EditorGUI.EndChangeCheck())
        {
            regexRuler = ".*" + input + ".*";
            matchShaderVariant = new List<ShaderVariantInfo>();
            for (int i = 0, count = shaderVariants.Count; i < count; i++)
            {
                var info = shaderVariants[i];
                if (info.shader == null) continue;
                if (Regex.IsMatch(info.shader.name, regexRuler, RegexOptions.IgnoreCase))
                {
                    matchShaderVariant.Add(info);
                }
            }
            if (isSortByCount)
                matchShaderVariant.Sort((a, b) => b.variantCount - a.variantCount);
            else
                matchShaderVariant.Sort((a, b) => a.path.CompareTo(b.path));
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("\n");

        if (toggleStateList == null || toggleStateList.Count != matchShaderVariant.Count)
        {
            toggleStateList = new List<bool>(matchShaderVariant.Count);
            for (int i = 0, count = matchShaderVariant.Count; i < count; i++)
            {
                toggleStateList.Add(false);
            }
        }
        for (int i = 0, count = matchShaderVariant.Count; i < count; i++)
        {
            var info = matchShaderVariant[i];
            if (info.shader == null) continue;
            GUIStyle style = new GUIStyle();
            style.richText = true;

            if (info.variantCount > 64)
                GUILayout.Label("<color=red>" + string.Format("shader:{0} \npath:{1}\nvariants:{2}", info.shader.name, info.path, info.variantCount) + "</color>", style);
            else
                GUILayout.Label(string.Format("shader:{0} \npath:{1}\nvariants:{2}", info.shader.name, info.path, info.variantCount, style));

            toggleStateList[i] = GUILayout.Toggle(toggleStateList[i], "show");
            if (toggleStateList[i])
                for (int n = 0, keywordCount = info.keyword.Count; n < keywordCount; n++)
                {
                    GUILayout.Label(string.Format("keyword:{0}    passtype:{1}", info.keyword[n], info.pass[n]));
                }
            GUILayout.Label("");
        }
    }

}