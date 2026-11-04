using System;
using System.Linq;
using Hullbreach.NetCode.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Hullbreach.Editor
{
    // repairs the missing assets without rebuilding the lobby or changing gameplay.
    // frob:doc docs/rendering.md
    public static class MultiplayerRenderingSetup
    {
        const string PipelinePath = "Assets/Settings/HullbreachPipeline.asset";
        const string RendererPath = "Assets/Settings/HullbreachRenderer.asset";

        // frob:doc docs/rendering.md
        [MenuItem("Hullbreach/Online/Repair multiplayer rendering")]
        public static void Repair()
        {
            EnsureRenderPipeline();
            Scene scene = EditorSceneManager.OpenScene(LobbySceneBuilder.GameScenePath, OpenSceneMode.Single);
            HullbreachNetCodeView view = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<HullbreachNetCodeView>(true)).Single();
            AssignSprites(view);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("MultiplayerRenderingSetup: pipeline and six multiplayer sprites are saved.");
        }

        internal static void EnsureRenderPipeline()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                    AssetDatabase.CreateFolder("Assets", "Settings");

                var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
                if (urp == null)
                {
                    var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                    if (renderer == null)
                    {
                        renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                        renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                            "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                        AssetDatabase.CreateAsset(renderer, RendererPath);
                    }
                    urp = UniversalRenderPipelineAsset.Create(renderer);
                    AssetDatabase.CreateAsset(urp, PipelinePath);
                }
                pipeline = urp;
                GraphicsSettings.defaultRenderPipeline = pipeline;
            }

            // preserve any valid quality override and fill only the missing references.
            int originalQuality = QualitySettings.GetQualityLevel();
            try
            {
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    if (QualitySettings.renderPipeline == null)
                        QualitySettings.renderPipeline = pipeline;
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(originalQuality, false);
            }
            AssetDatabase.SaveAssets();
        }

        internal static void AssignSprites(HullbreachNetCodeView view)
        {
            var serialized = new SerializedObject(view);
            SetSprite(serialized, "turretSprite", "TurretTest");
            SetSprite(serialized, "thursterSprite", "ThrusterTest");
            SetSprite(serialized, "armorSprite", "ArmorTest");
            SetSprite(serialized, "finSprite", "FinTest");
            SetSprite(serialized, "blockSprite", "BlockTest");
            SetSprite(serialized, "projectileSprite", "ProjectileTest");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetSprite(SerializedObject view, string field, string file)
        {
            SerializedProperty property = view.FindProperty(field);
            if (property.objectReferenceValue != null) return;
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Temp/" + file + ".png");
            if (sprite == null) throw new InvalidOperationException("Missing sprite: " + file);
            property.objectReferenceValue = sprite;
        }
    }
}
