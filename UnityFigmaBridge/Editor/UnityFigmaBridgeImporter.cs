﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Fonts;
using UnityFigmaBridge.Editor.Nodes;
using UnityFigmaBridge.Editor.PrototypeFlow;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;
using UnityFigmaBridge.Runtime.UI;
using Object = UnityEngine.Object;

namespace UnityFigmaBridge.Editor
{
    /// <summary>
    ///  Manages Figma importing and document creation
    /// </summary>
    public static class UnityFigmaBridgeImporter
    {
        
        /// <summary>
        /// The settings asset, containing preferences for importing
        /// </summary>
        private static UnityFigmaBridgeSettings s_UnityFigmaBridgeSettings;
        
        /// <summary>
        /// We'll cache the access token in editor Player prefs
        /// </summary>
        private const string FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY = "FIGMA_PERSONAL_ACCESS_TOKEN";

        public const string PROGRESS_BOX_TITLE = "Importing Figma Document";

        /// <summary>
        /// Figma imposes a limit on the number of images in a single batch. This is batch size
        /// (This is a bit of a guess - 650 is rejected)
        /// </summary>
        private const int MAX_SERVER_RENDER_IMAGE_BATCH_SIZE = 300;

        /// <summary>
        /// Cached personal access token, retrieved from PlayerPrefs
        /// </summary>
        private static string s_PersonalAccessToken;
        
        /// <summary>
        /// Active canvas used for construction
        /// </summary>
        private static Canvas s_SceneCanvas;

        /// <summary>
        /// The flowScreen controller to mange prototype functionality
        /// </summary>
        private static PrototypeFlowController s_PrototypeFlowController;

        /// <summary>
        /// The path where we save the cached Figma document JSON
        /// </summary>
        private const string FIGMA_DOCUMENT_CACHE_PATH = "Assets/FigmaCache";

        [MenuItem("Figma Bridge/Sync Document")]
        static void Sync()
        {
            SyncAsync();
        }
        
        /// <summary>
        /// Start the sync process using the current settings
        /// </summary>
        public static async void SyncAsync()
        {
            // Find the settings asset if it exists
            if (s_UnityFigmaBridgeSettings == null)
                s_UnityFigmaBridgeSettings = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
                
            SyncAsync(s_UnityFigmaBridgeSettings);
        }
        
        /// <summary>
        /// Start the sync process using the provided settings, with option for incremental update
        /// </summary>
        public static async void SyncAsync(UnityFigmaBridgeSettings settings, bool incrementalUpdate = false)
        {
            if (settings == null)
            {
                Debug.LogError("No settings provided for Figma import");
                return;
            }
            
            s_UnityFigmaBridgeSettings = settings;
            
            var requirementsMet = CheckRequirements();
            if (!requirementsMet) return;

            var figmaFile = await DownloadFigmaDocument(settings.FileId);
            if (figmaFile == null) return;

            // If incremental update, load the previous version for comparison
            FigmaFile previousFigmaFile = null;
            if (incrementalUpdate)
            {
                previousFigmaFile = LoadCachedFigmaDocument(settings.FileId);
                if (previousFigmaFile == null)
                {
                    Debug.LogWarning("No cached Figma document found. Performing full sync instead of incremental update.");
                    incrementalUpdate = false;
                }
            }

            // Save the current document for future incremental updates
            SaveFigmaDocument(settings.FileId, figmaFile);

            var pageNodeList = FigmaDataUtils.GetPageNodes(figmaFile);

            if (settings.OnlyImportSelectedPages)
            {
                var downloadPageNodeIdList = pageNodeList.Select(p => p.id).ToList();
                downloadPageNodeIdList.Sort();

                var settingsPageDataIdList = settings.PageDataList.Select(p => p.NodeId).ToList();
                settingsPageDataIdList.Sort();

                if (!settingsPageDataIdList.SequenceEqual(downloadPageNodeIdList))
                {
                    ReportError("The pages found in the Figma document have changed - check your settings file and Sync again when ready", "");
                    
                    // Apply the new page list to serialized data and select to allow the user to change
                    settings.RefreshForUpdatedPages(figmaFile);
                    Selection.activeObject = settings;
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssetIfDirty(settings);
                    AssetDatabase.Refresh();
                    
                    return;
                }
                
                var enabledPageIdList = settings.PageDataList.Where(p => p.Selected).Select(p => p.NodeId).ToList();

                if (enabledPageIdList.Count <= 0)
                {
                    ReportError("'Import Selected Pages' is selected, but no pages are selected for import", "");
                    SelectSettings();
                    return;
                }

                pageNodeList = pageNodeList.Where(p => enabledPageIdList.Contains(p.id)).ToList();
            }

            await ImportDocument(settings.FileId, figmaFile, pageNodeList, incrementalUpdate, previousFigmaFile);
        }

        /// <summary>
        /// Check to make sure all requirements are met before syncing
        /// </summary>
        /// <returns></returns>
        public static bool CheckRequirements() {
            
            // Find the settings asset if it exists
            if (s_UnityFigmaBridgeSettings == null)
                s_UnityFigmaBridgeSettings = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            
            if (s_UnityFigmaBridgeSettings == null)
            {
                if (
                    EditorUtility.DisplayDialog("No Unity Figma Bridge Settings File",
                        "Create a new Unity Figma bridge settings file? ", "Create", "Cancel"))
                {
                    s_UnityFigmaBridgeSettings =
                        UnityFigmaBridgeSettingsProvider.GenerateUnityFigmaBridgeSettingsAsset();
                }
                else
                {
                    return false;
                }
            }

            if (Shader.Find("TextMeshPro/Mobile/Distance Field")==null)
            {
                EditorUtility.DisplayDialog("Text Mesh Pro" ,"You need to install TestMeshPro Essentials. Use Window->Text Mesh Pro->Import TMP Essential Resources","OK");
                return false;
            }
            
            if (s_UnityFigmaBridgeSettings.FileId.Length == 0)
            {
                EditorUtility.DisplayDialog("Missing Figma Document" ,"Figma Document Url is not valid, please enter valid URL","OK");
                return false;
            }
            
            // Get stored personal access key
            s_PersonalAccessToken = PlayerPrefs.GetString(FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY);

            if (string.IsNullOrEmpty(s_PersonalAccessToken))
            {
                var setToken = RequestPersonalAccessToken();
                if (!setToken) return false;
            }
            
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("Figma Unity Bridge Importer","Please exit play mode before importing", "OK");
                return false;
            }
            
            // Check all requirements for run time if required
            if (s_UnityFigmaBridgeSettings.BuildPrototypeFlow)
            {
                if (!CheckRunTimeRequirements())
                    return false;
            }
            
            return true;
            
        }


        private static bool CheckRunTimeRequirements()
        {
            if (string.IsNullOrEmpty(s_UnityFigmaBridgeSettings.RunTimeAssetsScenePath))
            {
                if (
                    EditorUtility.DisplayDialog("No Figma Bridge Scene set",
                        "Use current scene for generating prototype flow? ", "OK", "Cancel"))
                {
                    var currentScene = SceneManager.GetActiveScene();
                    s_UnityFigmaBridgeSettings.RunTimeAssetsScenePath = currentScene.path;
                    EditorUtility.SetDirty(s_UnityFigmaBridgeSettings);
                    AssetDatabase.SaveAssetIfDirty(s_UnityFigmaBridgeSettings);
                }
                else
                {
                    return false;
                }
            }
            
            // If current scene doesnt match, switch
            if (SceneManager.GetActiveScene().path != s_UnityFigmaBridgeSettings.RunTimeAssetsScenePath)
            {
                if (EditorUtility.DisplayDialog("Figma Bridge Scene",
                        "Current Scene doesnt match Runtime asset scene - switch scenes?", "OK", "Cancel"))
                {
                    EditorSceneManager.OpenScene(s_UnityFigmaBridgeSettings.RunTimeAssetsScenePath);
                }
                else
                {
                    return false;
                }
            }
            
            // Find a canvas in the active scene
            s_SceneCanvas = Object.FindObjectOfType<Canvas>();
            
            // If doesnt exist create new one
            if (s_SceneCanvas == null)
            {
                s_SceneCanvas = CreateCanvas(true);
            }
            
            // If we are building a prototype, ensure we have a UI Controller component
            s_PrototypeFlowController = s_SceneCanvas.GetComponent<PrototypeFlowController>();
            if (s_PrototypeFlowController== null)
                s_PrototypeFlowController = s_SceneCanvas.gameObject.AddComponent<PrototypeFlowController>();
            
            return true;
        }

        [MenuItem("Figma Bridge/Select Settings File")]
        static void SelectSettings()
        {
            var bridgeSettings=UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            Selection.activeObject = bridgeSettings;
        }

        [MenuItem("Figma Bridge/Set Personal Access Token")]
        static void SetPersonalAccessToken()
        {
            RequestPersonalAccessToken();
        }
        
        /// <summary>
        /// Launch window to request personal access token
        /// </summary>
        /// <returns></returns>
        static bool RequestPersonalAccessToken()
        {
            s_PersonalAccessToken = PlayerPrefs.GetString(FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY);
            var newAccessToken = EditorInputDialog.Show( "Personal Access Token", "Please enter your Figma Personal Access Token (you can create in the 'Developer settings' page)",s_PersonalAccessToken);
            if (!string.IsNullOrEmpty(newAccessToken))
            {
                s_PersonalAccessToken = newAccessToken;
                Debug.Log( $"New access token set {s_PersonalAccessToken}");
                PlayerPrefs.SetString(FIGMA_PERSONAL_ACCESS_TOKEN_PREF_KEY,s_PersonalAccessToken);
                PlayerPrefs.Save();
                return true;
            }

            return false;
        }


        private static Canvas CreateCanvas(bool createEventSystem)
        {
            // Canvas
            var canvasGameObject = new GameObject("Canvas");
            var canvas=canvasGameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGameObject.AddComponent<GraphicRaycaster>();

            if (!createEventSystem) return canvas;

            var existingEventSystem = Object.FindObjectOfType<EventSystem>();
            if (existingEventSystem == null)
            {
                // Create new event system
                var eventSystemGameObject = new GameObject("EventSystem");
                existingEventSystem=eventSystemGameObject.AddComponent<EventSystem>();
            }

            var pointerInputModule = Object.FindObjectOfType<PointerInputModule>();
            if (pointerInputModule == null)
            {
                // TODO - Allow for new input system?
                existingEventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }

            return canvas;
        }
        

        private static void ReportError(string message,string error)
        {
            EditorUtility.DisplayDialog("Unity Figma Bridge Error",message,"Ok");
            Debug.LogWarning($"{message}\n {error}\n");
        }

        public static async Task<FigmaFile> DownloadFigmaDocument(string fileId)
        {
            // Download figma document
            EditorUtility.DisplayProgressBar(PROGRESS_BOX_TITLE, $"Downloading file", 0);
            try
            {
                var figmaTask = FigmaApiUtils.GetFigmaDocument(fileId, s_PersonalAccessToken, true);
                await figmaTask;
                return figmaTask.Result;
            }
            catch (Exception e)
            {
                ReportError(
                    "Error downloading Figma document - Check your personal access key and document url are correct",
                    e.ToString());
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return null;
        }

        private static async Task ImportDocument(string fileId, FigmaFile figmaFile, List<Node> downloadPageNodeList, 
            bool incrementalUpdate = false, FigmaFile previousFigmaFile = null)
        {
            // Initialize paths with domain from settings
            FigmaPaths.InitializeWithSettings(s_UnityFigmaBridgeSettings);

            // Build a list of page IDs to download
            var downloadPageIdList = downloadPageNodeList.Select(p => p.id).ToList();
            
            // Ensure we have all required directories, and remove existing files
            // TODO - Once we move to processing only differences, we won't remove existing files
            FigmaPaths.CreateRequiredDirectories();
            
            // Next build a list of all externally referenced components not included in the document (eg
            // from external libraries) and download
            var externalComponentList = FigmaDataUtils.FindMissingComponentDefinitions(figmaFile);
            
            // TODO - Implement external components
            // This is currently not working as only returns a depth of 1 of returned nodes. Need to get original files too
            /*
            FigmaFileNodes activeExternalComponentsData=null;
            if (externalComponentList.Count > 0)
            {
                EditorUtility.DisplayProgressBar(PROGRESS_BOX_TITLE, $"Getting external component data", 0);
                try
                {
                    var figmaTask = FigmaApiUtils.GetFigmaFileNodes(fileId, s_PersonalAccessToken,externalComponentList);
                    await figmaTask;
                    activeExternalComponentsData = figmaTask.Result;
                }
                catch (Exception e)
                {
                    EditorUtility.ClearProgressBar();
                    ReportError("Error downloading external component Data",e.ToString());
                    return;
                }
            }
            */

            // For any missing component definitions, we are going to find the first instance and switch it to be
            // The source component. This has to be done early to ensure download of server images
            //FigmaFileUtils.ReplaceMissingComponents(figmaFile,externalComponentList);
            
            // Some of the nodes, we'll want to identify to use Figma server side rendering (eg vector shapes, SVGs)
            var serverRenderNodes = FigmaDataUtils.FindAllServerRenderNodesInFile(figmaFile, externalComponentList, downloadPageIdList);
            
            // If incremental update, filter to only nodes that have changed
            if (incrementalUpdate && previousFigmaFile != null)
            {
                serverRenderNodes = FilterChangedNodes(serverRenderNodes, previousFigmaFile);
            }
            
            // Request a render of these nodes on the server if required
            var serverRenderData = new List<FigmaServerRenderData>();
            if (serverRenderNodes.Count > 0)
            {
                var allNodeIds = serverRenderNodes.Select(serverRenderNode => serverRenderNode.SourceNode.id).ToList();
                // As the API has an upper limit of images that can be rendered in a single request, we'll need to batch
                var batchCount = Mathf.CeilToInt((float)allNodeIds.Count / MAX_SERVER_RENDER_IMAGE_BATCH_SIZE);
                for (var i = 0; i < batchCount; i++)
                {
                    var startIndex = i * MAX_SERVER_RENDER_IMAGE_BATCH_SIZE;
                    var nodeBatch = allNodeIds.GetRange(startIndex,
                        Mathf.Min(MAX_SERVER_RENDER_IMAGE_BATCH_SIZE, allNodeIds.Count - startIndex));
                    var serverNodeCsvList = string.Join(",", nodeBatch);
                    EditorUtility.DisplayProgressBar(PROGRESS_BOX_TITLE, $"Downloading server-rendered image data {i+1}/{batchCount}",(float)i/(float)batchCount);
                    try
                    {
                        var figmaTask = FigmaApiUtils.GetFigmaServerRenderData(fileId, s_PersonalAccessToken,
                            serverNodeCsvList, s_UnityFigmaBridgeSettings.ServerRenderImageScale);
                        await figmaTask;
                        serverRenderData.Add(figmaTask.Result);
                    }
                    catch (Exception e)
                    {
                        EditorUtility.ClearProgressBar();
                        ReportError("Error downloading Figma Server Render Image Data", e.ToString());
                        return;
                    }
                }
            }

            // Make sure that existing downloaded assets are in the correct format
            FigmaApiUtils.CheckExistingAssetProperties();
            
            // Track fills that are actually used. This is needed as FIGMA has a way of listing any bitmap used rather than active 
            var foundImageFills = FigmaDataUtils.GetAllImageFillIdsFromFile(figmaFile,downloadPageIdList);
            
            // Get image fill data for the document (list of urls to download any bitmap data used)
            FigmaImageFillData activeFigmaImageFillData; 
            EditorUtility.DisplayProgressBar(PROGRESS_BOX_TITLE, $"Downloading image fill data", 0);
            try
            {
                var figmaTask = FigmaApiUtils.GetDocumentImageFillData(fileId, s_PersonalAccessToken);
                await figmaTask;
                activeFigmaImageFillData = figmaTask.Result;
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                ReportError("Error downloading Figma Image Fill Data", e.ToString());
                return;
            }
            
            // Generate a list of all items that need to be downloaded
            var downloadList =
                FigmaApiUtils.GenerateDownloadQueue(activeFigmaImageFillData, foundImageFills, serverRenderData, serverRenderNodes);

            // Download all required files
            await FigmaApiUtils.DownloadFiles(downloadList, s_UnityFigmaBridgeSettings);
            

            // Generate font mapping data
            var figmaFontMapTask = FontManager.GenerateFontMapForDocument(figmaFile,
                s_UnityFigmaBridgeSettings.EnableGoogleFontsDownloads);
            await figmaFontMapTask;
            var fontMap = figmaFontMapTask.Result;


            var componentData = new FigmaBridgeComponentData
            { 
                MissingComponentDefinitionsList = externalComponentList, 
            };
            
            // Stores necessary importer data needed for document generator.
            var figmaBridgeProcessData = new FigmaImportProcessData
            {
                Settings = s_UnityFigmaBridgeSettings,
                SourceFile = figmaFile,
                PreviousSourceFile = previousFigmaFile,
                ComponentData = componentData,
                ServerRenderNodes = serverRenderNodes,
                PrototypeFlowController = s_PrototypeFlowController,
                FontMap = fontMap,
                PrototypeFlowStartPoints = FigmaDataUtils.GetAllPrototypeFlowStartingPoints(figmaFile),
                SelectedPagesForImport = downloadPageNodeList,
                NodeLookupDictionary = FigmaDataUtils.BuildNodeLookupDictionary(figmaFile),
                IsIncrementalUpdate = incrementalUpdate
            };
            
            
            // Clear the existing screens on the flowScreen controller if not incremental update
            if (s_UnityFigmaBridgeSettings.BuildPrototypeFlow)
            {
                if (figmaBridgeProcessData.PrototypeFlowController)
                {
                    if (!incrementalUpdate)
                    {
                        figmaBridgeProcessData.PrototypeFlowController.ClearFigmaScreens();
                    }
                }
            }
            else
            {
                s_SceneCanvas = CreateCanvas(false);
            }

            try
            {
                FigmaAssetGenerator.BuildFigmaFile(s_SceneCanvas, figmaBridgeProcessData);
            }
            catch (Exception e)
            {
                ReportError("Error generating Figma document. Check log for details", e.ToString());
                EditorUtility.ClearProgressBar();
                CleanUpPostGeneration();
                return;
            }
           
            
            // Lastly, for prototype mode, instantiate the default flowScreen and set the scaler up appropriately
            if (s_UnityFigmaBridgeSettings.BuildPrototypeFlow)
            {
                // Make sure all required default elements are present
                var screenController = figmaBridgeProcessData.PrototypeFlowController;
                
                // Find default flow start position
                screenController.PrototypeFlowInitialScreenId =  FigmaDataUtils.FindPrototypeFlowStartScreenId(figmaBridgeProcessData.SourceFile);;

                if (screenController.ScreenParentTransform == null)
                    screenController.ScreenParentTransform=UnityUiUtils.CreateRectTransform("ScreenParentTransform",
                        figmaBridgeProcessData.PrototypeFlowController.transform as RectTransform);

                if (screenController.TransitionEffect == null)
                {
                    return; // remove after, during local development I dont care about TransitionEffect
                    // Instantiate and apply the default transition effect (loaded from package assets folder)
                    var defaultTransitionAnimationEffect = AssetDatabase.LoadAssetAtPath("Packages/com.simonoliver.unityfigma/UnityFigmaBridge/Assets/TransitionFadeToBlack.prefab", typeof(GameObject)) as GameObject;
                    var transitionObject = (GameObject) PrefabUtility.InstantiatePrefab(defaultTransitionAnimationEffect,
                        screenController.transform.transform);
                    screenController.TransitionEffect =
                        transitionObject.GetComponent<TransitionEffect>();
                    
                    UnityUiUtils.SetTransformFullStretch(transitionObject.transform as RectTransform);
                }

                // Set start flowScreen on stage by default                
                var defaultScreenData = figmaBridgeProcessData.PrototypeFlowController.StartFlowScreen;
                if (defaultScreenData != null)
                {
                    var defaultScreenTransform = defaultScreenData.FigmaScreenPrefab.transform as RectTransform;
                    if (defaultScreenTransform != null)
                    {
                        var defaultSize = defaultScreenTransform.sizeDelta;
                        var canvasScaler = s_SceneCanvas.GetComponent<CanvasScaler>();
                        if (canvasScaler == null) canvasScaler = s_SceneCanvas.gameObject.AddComponent<CanvasScaler>();
                        canvasScaler.referenceResolution = defaultSize;
                        // If we are a vertical template, drive by width
                        canvasScaler.matchWidthOrHeight = (defaultSize.x>defaultSize.y) ? 1f : 0f; // Use height as driver
                        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    }

                    var screenInstance=(GameObject)PrefabUtility.InstantiatePrefab(defaultScreenData.FigmaScreenPrefab, figmaBridgeProcessData.PrototypeFlowController.ScreenParentTransform);
                    figmaBridgeProcessData.PrototypeFlowController.SetCurrentScreen(screenInstance,defaultScreenData.FigmaNodeId,true);
                }
                // Write CS file with references to flowScreen name
                if (s_UnityFigmaBridgeSettings.CreateScreenNameCSharpFile) ScreenNameCodeGenerator.WriteScreenNamesCodeFile(figmaBridgeProcessData.ScreenPrefabs);
            }
            CleanUpPostGeneration();
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        /// <summary>
        ///  Clean up any leftover assets post-generation
        /// </summary>
        private static void CleanUpPostGeneration()
        {
            if (!s_UnityFigmaBridgeSettings.BuildPrototypeFlow)
            {
                // Destroy temporary canvas
                Object.DestroyImmediate(s_SceneCanvas.gameObject);
            }
        }

        /// <summary>
        /// Saves the Figma document to a cache file
        /// </summary>
        private static void SaveFigmaDocument(string fileId, FigmaFile figmaFile)
        {
            try
            {
                // Ensure the cache directory exists
                if (!Directory.Exists(FIGMA_DOCUMENT_CACHE_PATH))
                {
                    Directory.CreateDirectory(FIGMA_DOCUMENT_CACHE_PATH);
                }

                string filePath = Path.Combine(FIGMA_DOCUMENT_CACHE_PATH, $"{fileId}.json");
                string jsonContent = JsonUtility.ToJson(figmaFile, true);
                File.WriteAllText(filePath, jsonContent);
                
                Debug.Log($"Figma document cached to {filePath}");
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save Figma document cache: {e.Message}");
            }
        }

        /// <summary>
        /// Loads a previously cached Figma document
        /// </summary>
        private static FigmaFile LoadCachedFigmaDocument(string fileId)
        {
            try
            {
                string filePath = Path.Combine(FIGMA_DOCUMENT_CACHE_PATH, $"{fileId}.json");
                if (!File.Exists(filePath))
                {
                    return null;
                }

                string jsonContent = File.ReadAllText(filePath);
                return JsonUtility.FromJson<FigmaFile>(jsonContent);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load cached Figma document: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Filter server render nodes to only include those that have changed since the previous version
        /// </summary>
        private static List<ServerRenderNodeData> FilterChangedNodes(List<ServerRenderNodeData> allNodes, FigmaFile previousFile)
        {
            var changedNodes = new List<ServerRenderNodeData>();
            var previousNodeLookup = FigmaDataUtils.BuildNodeLookupDictionary(previousFile);
            
            foreach (var node in allNodes)
            {
                string nodeId = node.SourceNode.id;
                
                // If node didn't exist before, it's changed
                if (!previousNodeLookup.ContainsKey(nodeId))
                {
                    changedNodes.Add(node);
                    continue;
                }
                
                // Compare node properties to see if it changed
                var previousNode = previousNodeLookup[nodeId];
                if (HasNodeChanged(node.SourceNode, previousNode))
                {
                    changedNodes.Add(node);
                }
            }
            
            Debug.Log($"Filtered {allNodes.Count} nodes to {changedNodes.Count} changed nodes for incremental update");
            return changedNodes;
        }

        /// <summary>
        /// Check if a node has changed compared to its previous version
        /// </summary>
        private static bool HasNodeChanged(Node currentNode, Node previousNode)
        {
            // Compare basic properties
            if (currentNode.name != previousNode.name) return true;
            if (currentNode.visible != previousNode.visible) return true;
            if (currentNode.type != previousNode.type) return true;
            
            // Compare transforms
            if (currentNode.absoluteBoundingBox.x != previousNode.absoluteBoundingBox.x) return true;
            if (currentNode.absoluteBoundingBox.y != previousNode.absoluteBoundingBox.y) return true;
            if (currentNode.absoluteBoundingBox.width != previousNode.absoluteBoundingBox.width) return true;
            if (currentNode.absoluteBoundingBox.height != previousNode.absoluteBoundingBox.height) return true;
            
            // Compare fills
            if (currentNode.fills?.Length != previousNode.fills?.Length) return true;
            if (currentNode.fills != null && previousNode.fills != null)
            {
                for (int i = 0; i < currentNode.fills.Length; i++)
                {
                    if (currentNode.fills[i].type != previousNode.fills[i].type) return true;
                    if (currentNode.fills[i].visible != previousNode.fills[i].visible) return true;
                    if (currentNode.fills[i].opacity != previousNode.fills[i].opacity) return true;
                    // Compare colors if fill type is SOLID
                    if (currentNode.fills[i].type == Paint.PaintType.SOLID)
                    {
                        if (currentNode.fills[i].color.r != previousNode.fills[i].color.r) return true;
                        if (currentNode.fills[i].color.g != previousNode.fills[i].color.g) return true;
                        if (currentNode.fills[i].color.b != previousNode.fills[i].color.b) return true;
                        if (currentNode.fills[i].color.a != previousNode.fills[i].color.a) return true;
                    }
                    
                    // Compare image references if fill type is IMAGE
                    if (currentNode.fills[i].type == Paint.PaintType.IMAGE)
                    {
                        if (currentNode.fills[i].imageRef != previousNode.fills[i].imageRef) return true;
                    }
                }
            }
            
            // Compare strokes
            if (currentNode.strokes?.Length != previousNode.strokes?.Length) return true;
            
            // Compare children recursively if this is a container
            if (currentNode.children != null && previousNode.children != null)
            {
                if (currentNode.children.Length != previousNode.children.Length) return true;
                
                // We won't do deep comparison of children here as that would be expensive
                // Instead, we'll rely on the node ID comparison in the main filter method
            }
            
            // For text nodes, compare text content and style
            if (currentNode.type == NodeType.TEXT)
            {
                if (currentNode.characters != previousNode.characters) return true;
                if (currentNode.style.fontSize != previousNode.style.fontSize) return true;
                if (currentNode.style.fontFamily != previousNode.style.fontFamily) return true;
                if (currentNode.style.fontWeight != previousNode.style.fontWeight) return true;
            }
            
            return false;
        }

        [MenuItem("Figma Bridge/Update Document")]
        static void Update()
        {
            UpdateAsync();
        }

        /// <summary>
        /// Start the incremental update process using the current settings
        /// </summary>
        public static async void UpdateAsync()
        {
            // Find the settings asset if it exists
            if (s_UnityFigmaBridgeSettings == null)
                s_UnityFigmaBridgeSettings = UnityFigmaBridgeSettingsProvider.FindUnityBridgeSettingsAsset();
            
            SyncAsync(s_UnityFigmaBridgeSettings, true);
        }
    }
}
