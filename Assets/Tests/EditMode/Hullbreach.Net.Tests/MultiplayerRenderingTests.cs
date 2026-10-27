#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Hullbreach.Net.Tests
{
    public sealed class MultiplayerRenderingTests
    {
        const string ScenePath = "Assets/Scenes/MultiplayerGame.unity";

        [Test]
        public void MultiplayerSceneHasAllShipAndProjectileSprites()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                Component view = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                    .Single(component => component != null && component.GetType().Name == "HullbreachNetCodeView");
                var serialized = new SerializedObject(view);
                foreach (string field in new[]
                {
                    "turretSprite", "thursterSprite", "armorSprite",
                    "finSprite", "blockSprite", "projectileSprite"
                })
                {
                    Sprite sprite = serialized.FindProperty(field).objectReferenceValue as Sprite;
                    Assert.IsNotNull(sprite, field + " must be assigned or the renderer draws nothing");
                    Assert.IsFalse(string.IsNullOrEmpty(AssetDatabase.GetAssetPath(sprite)));
                    Assert.Greater(sprite.bounds.size.x, 0f);
                    Assert.Greater(sprite.bounds.size.y, 0f);
                }
            }
            finally
            {
                if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void GraphicsAndQualitySettingsHaveSavedPipelinesAndRenderers()
        {
            AssertPipeline(GraphicsSettings.defaultRenderPipeline, "graphics");
            for (int i = 0; i < QualitySettings.names.Length; i++)
                AssertPipeline(QualitySettings.GetRenderPipelineAssetAt(i), QualitySettings.names[i]);
        }

        static void AssertPipeline(RenderPipelineAsset pipeline, string name)
        {
            Assert.IsNotNull(pipeline, name + " references a missing render pipeline asset");
            Assert.IsFalse(string.IsNullOrEmpty(AssetDatabase.GetAssetPath(pipeline)));
            Assert.AreEqual("UniversalRenderPipelineAsset", pipeline.GetType().Name);
            var serialized = new SerializedObject(pipeline);
            SerializedProperty renderers = serialized.FindProperty("m_RendererDataList");
            int defaultIndex = serialized.FindProperty("m_DefaultRendererIndex").intValue;
            Assert.That(defaultIndex, Is.InRange(0, renderers.arraySize - 1));
            Object renderer = renderers.GetArrayElementAtIndex(defaultIndex).objectReferenceValue;
            Assert.IsNotNull(renderer, name + " has no default renderer");
            Assert.IsFalse(string.IsNullOrEmpty(AssetDatabase.GetAssetPath(renderer)));
        }
    }
}
#endif
