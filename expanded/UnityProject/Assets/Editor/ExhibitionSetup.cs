using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ExhibitionSetup
{
    [MenuItem("Resonance/Open Exhibition Scene")]
    [MenuItem("Resonance/Open Animal Scene")]
    public static void Prepare()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        const string scenePath="Assets/Resonance.unity";
        if(!System.IO.File.Exists(scenePath))
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var view=new GameObject("Exhibition Camera").AddComponent<Camera>(); view.tag="MainCamera";
            var root=new GameObject("Resonance / Azure Kinect");
            root.AddComponent<ExhibitionController>(); root.AddComponent<ExhibitionKinectInput>();
            EditorSceneManager.SaveScene(scene,scenePath);
        }
        else if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=scenePath)
            EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
        PlayerSettings.productName="ResonanceExhibition";
        ConfigureBuild(scenePath);
    }

    [MenuItem("Resonance/Open Environment Scene")]
    public static void PrepareEnvironment()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) return;
        const string scenePath="Assets/SpringEnvironment.unity";
        if(!System.IO.File.Exists(scenePath))
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var view=new GameObject("Environment Camera").AddComponent<Camera>();view.tag="MainCamera";
            var root=new GameObject("Spring Environment / Azure Kinect");
            root.AddComponent<SpringEnvironmentController>();root.AddComponent<ExhibitionKinectInput>();
            EditorSceneManager.SaveScene(scene,scenePath);
        }
        else if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=scenePath)
            EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
        PlayerSettings.productName="SpringEnvironmentExhibition";
        ConfigureBuild(scenePath);
    }

    static void ConfigureBuild(string scenePath)
    {
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(scenePath,true)};
        PlayerSettings.fullScreenMode=FullScreenMode.FullScreenWindow;
        PlayerSettings.defaultScreenWidth=1920; PlayerSettings.defaultScreenHeight=1080;
        var settings=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        foreach(string path in new[]{"Assets/ResonanceLight.shader","Assets/ResonanceShadow.shader"})
        {
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
            if(settings.Length==0||shader==null) continue;
            var serialized=new SerializedObject(settings[0]);
            var included=serialized.FindProperty("m_AlwaysIncludedShaders");
            bool present=false;
            for(int i=0;i<included.arraySize;i++) if(included.GetArrayElementAtIndex(i).objectReferenceValue==shader) present=true;
            if(!present) { int i=included.arraySize; included.InsertArrayElementAtIndex(i); included.GetArrayElementAtIndex(i).objectReferenceValue=shader; serialized.ApplyModifiedPropertiesWithoutUndo(); }
        }
        AssetDatabase.SaveAssets();
    }
}
