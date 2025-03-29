using UnityEngine;
using UnityEditor;
using System;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Newtonsoft.Json;
using UnityEngine.UIElements;
using System.Collections.Generic;
using UnityFigmaBridge.Editor.AIGenerator;

public class BedrockUIGenerator : EditorWindow
{
    private string _prompt = "Crie um layout de menu principal para um jogo com botões para Jogar, Opções e Sair";
    private string _generatedCode = "";
    private string _modelId = "anthropic.claude-v2"; // Modelo padrão
    private bool _isGenerating = false;
    private string _awsAccessKey = "";
    private string _awsSecretKey = "";
    private string _awsRegion = "us-east-1";
    private string _uiName = "UI"; // Novo campo para o nome dos arquivos
    private bool _autoSave = true; // Nova flag para salvar automaticamente
    private bool _useCurrentScene = true; // Nova flag para usar UIDocument na cena atual
    private bool _generateScript = false; // Nova flag para controlar a geração do script C#

    [MenuItem("Window/AWS Bedrock/UI Generator")]
    public static void ShowWindow()
    {
        GetWindow<BedrockUIGenerator>("Bedrock UI Generator");
    }

    private void OnEnable()
    {
        // Carregar configurações salvas
        _awsAccessKey = EditorPrefs.GetString("BedrockUIGenerator_AccessKey", "");
        _awsSecretKey = EditorPrefs.GetString("BedrockUIGenerator_SecretKey", "");
        _awsRegion = EditorPrefs.GetString("BedrockUIGenerator_Region", "us-east-1");
        _modelId = EditorPrefs.GetString("BedrockUIGenerator_ModelId", "anthropic.claude-v2");
        _uiName = EditorPrefs.GetString("BedrockUIGenerator_UIName", "UI"); // Carregar nome salvo
        _autoSave = EditorPrefs.GetBool("BedrockUIGenerator_AutoSave", true); // Carregar configuração de auto-save
        _useCurrentScene = EditorPrefs.GetBool("BedrockUIGenerator_UseCurrentScene", true); // Carregar configuração de uso da cena atual
        _generateScript = EditorPrefs.GetBool("BedrockUIGenerator_GenerateScript", false); // Carregar configuração de geração de script
    }

    private void OnGUI()
    {
        GUILayout.Label("AWS Bedrock UI Generator", EditorStyles.boldLabel);
        
        EditorGUILayout.Space();
        
        // AWS Credentials
        EditorGUILayout.LabelField("AWS Credentials", EditorStyles.boldLabel);
        _awsAccessKey = EditorGUILayout.TextField("Access Key", _awsAccessKey);
        _awsSecretKey = EditorGUILayout.PasswordField("Secret Key", _awsSecretKey);
        _awsRegion = EditorGUILayout.TextField("Region", _awsRegion);
        
        // Salvar credenciais
        if (GUILayout.Button("Salvar Credenciais"))
        {
            EditorPrefs.SetString("BedrockUIGenerator_AccessKey", _awsAccessKey);
            EditorPrefs.SetString("BedrockUIGenerator_SecretKey", _awsSecretKey);
            EditorPrefs.SetString("BedrockUIGenerator_Region", _awsRegion);
            EditorUtility.DisplayDialog("Sucesso", "Credenciais salvas com sucesso!", "OK");
        }
        
        EditorGUILayout.Space();
        
        // Modelo seleção
        EditorGUILayout.LabelField("Modelo", EditorStyles.boldLabel);
        string[] models = new string[] { 
            "anthropic.claude-v2", 
            "anthropic.claude-instant-v1",
            "amazon.titan-text-express-v1",
            "amazon.nova-pro-v1:0"
        };
        
        int selectedIndex = Array.IndexOf(models, _modelId);
        selectedIndex = EditorGUILayout.Popup("Modelo", selectedIndex >= 0 ? selectedIndex : 0, models);
        if (selectedIndex >= 0)
        {
            _modelId = models[selectedIndex];
            EditorPrefs.SetString("BedrockUIGenerator_ModelId", _modelId);
        }
        
        EditorGUILayout.Space();
        
        // Prompt para gerar UI
        EditorGUILayout.LabelField("Prompt para UI", EditorStyles.boldLabel);
        _prompt = EditorGUILayout.TextArea(_prompt, GUILayout.Height(100));
        
        // Campo para o nome da UI
        EditorGUILayout.LabelField("Nome da UI", EditorStyles.boldLabel);
        _uiName = EditorGUILayout.TextField("Nome", _uiName);
        if (string.IsNullOrWhiteSpace(_uiName))
        {
            _uiName = "UI"; // Valor padrão se estiver vazio
        }
        
        // Opção para salvar automaticamente
        _autoSave = EditorGUILayout.Toggle("Geração Automática", _autoSave);
        EditorPrefs.SetBool("BedrockUIGenerator_AutoSave", _autoSave);
        
        // Opção para usar UIDocument na cena atual
        _useCurrentScene = EditorGUILayout.Toggle("Usar UIDocument na Cena Atual", _useCurrentScene);
        EditorPrefs.SetBool("BedrockUIGenerator_UseCurrentScene", _useCurrentScene);
        
        // Opção para gerar script C#
        _generateScript = EditorGUILayout.Toggle("Gerar Script C#", _generateScript);
        EditorPrefs.SetBool("BedrockUIGenerator_GenerateScript", _generateScript);
        
        EditorGUILayout.Space();
        
        GUI.enabled = !_isGenerating && !string.IsNullOrEmpty(_awsAccessKey) && !string.IsNullOrEmpty(_awsSecretKey);
        if (GUILayout.Button(_isGenerating ? "Gerando..." : "Gerar UI Code"))
        {
            GenerateUICode();
        }
        GUI.enabled = true;
        
        EditorGUILayout.Space();
        
        // Código gerado
        EditorGUILayout.LabelField("Código Gerado", EditorStyles.boldLabel);
        if (!string.IsNullOrEmpty(_generatedCode))
        {
            _generatedCode = EditorGUILayout.TextArea(_generatedCode, GUILayout.Height(300));
            
            EditorGUILayout.Space();
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Copiar Código"))
            {
                EditorGUIUtility.systemCopyBuffer = _generatedCode;
                EditorUtility.DisplayDialog("Copiado", "Código copiado para a área de transferência!", "OK");
            }
            
            if (GUILayout.Button("Salvar Código Gerado"))
            {
                SaveGeneratedCode();
            }
            
            EditorGUILayout.EndHorizontal();
        }
    }

    private async void GenerateUICode()
    {
        if (_isGenerating) return;
        
        _isGenerating = true;
        
        try
        {
            var bedrockService = new BedrockService(_awsAccessKey, _awsSecretKey, _awsRegion, _modelId);
            
            string promptText = $@"
<instructions>
Gere um código C# para Unity UI Toolkit que implemente o seguinte layout: {_prompt}.
</instructions>

<formatting>
O código gerado deve incluir:
- Código UXML dentro da tag `<UXML>`.
- Código Unity Style Sheets (USS) dentro da tag `<CSS>`.
- O código C# necessário para manipulação da UI Toolkit.

Não utilize as tags <C#>, <CSS> ou <UXML> em outras partes da resposta que não sejam as tags de início e fim.
Certifique-se de que os elementos e estilos estejam bem estruturados e utilizem boas práticas de organização no UI Toolkit.
</formatting>

<example>
<UXML>
<!-- Código UXML gerado aqui -->
</UXML>

<CSS>
/* Código USS gerado aqui */
</CSS>

<C#>
// Código C# gerado aqui
</C#>
</example>
";

            // Chamar o serviço para gerar o conteúdo
            _generatedCode = await bedrockService.GenerateContent(promptText);
            
            // Salvar automaticamente se a opção estiver ativada
            if (_autoSave && !string.IsNullOrEmpty(_generatedCode))
            {
                SaveGeneratedCode();
            }
            
            // Atualizar a UI
            Repaint();
        }
        catch (Exception ex)
        {
            Debug.LogError($"Erro ao chamar AWS Bedrock: {ex.Message}");
            EditorUtility.DisplayDialog("Erro", $"Falha ao gerar código: {ex.Message}", "OK");
            _generatedCode = $"Erro: {ex.Message}";
        }
        finally
        {
            _isGenerating = false;
        }
    }

    private void SaveGeneratedCode()
    {
        try
        {
            // Create directories if they don't exist
            string resourcesPath = Path.Combine(Application.dataPath, "Resources");
            string scriptsPath = Path.Combine(Application.dataPath, "Scripts");
            
            if (!Directory.Exists(resourcesPath))
            {
                Directory.CreateDirectory(resourcesPath);
            }
            
            if (!Directory.Exists(scriptsPath))
            {
                Directory.CreateDirectory(scriptsPath);
            }
            
            // Usar o nome fornecido ou o padrão "UI"
            string fileName = string.IsNullOrWhiteSpace(_uiName) ? "UI" : _uiName;
            
            // Extract UXML content
            string uxmlContent = ExtractContent("<UXML>", "</UXML>");
            string uxmlFilePath = "";
            
            if (!string.IsNullOrEmpty(uxmlContent))
            {
                // Adicionar referência ao arquivo USS se não existir
                string styleReference = $"<Style src=\"{fileName}Styles.uss\" />";
                if (!uxmlContent.Contains("<Style src=") && !uxmlContent.Contains("<style src="))
                {
                    // Encontrar a tag ui:UXML
                    int uiUxmlIndex = uxmlContent.IndexOf("<ui:UXML");
                    if (uiUxmlIndex > -1)
                    {
                        int uiUxmlEndIndex = uxmlContent.IndexOf(">", uiUxmlIndex);
                        if (uiUxmlEndIndex > -1)
                        {
                            // Inserir a referência ao estilo como primeiro elemento dentro da tag ui:UXML
                            uxmlContent = uxmlContent.Insert(uiUxmlEndIndex + 1, "\n    " + styleReference);
                        }
                    }
                    else
                    {
                        // Tentar com <UXML> sem prefixo ui:
                        int uxmlIndex = uxmlContent.IndexOf("<UXML");
                        if (uxmlIndex > -1)
                        {
                            int uxmlEndIndex = uxmlContent.IndexOf(">", uxmlIndex);
                            if (uxmlEndIndex > -1)
                            {
                                // Inserir a referência ao estilo como primeiro elemento dentro da tag UXML
                                uxmlContent = uxmlContent.Insert(uxmlEndIndex + 1, "\n    " + styleReference);
                            }
                        }
                        else
                        {
                            // Se não encontrar nenhuma tag UXML, apenas adicionar ao início
                            uxmlContent = styleReference + "\n" + uxmlContent;
                        }
                    }
                }
                
                uxmlFilePath = Path.Combine(resourcesPath, fileName + "Layout.uxml");
                File.WriteAllText(uxmlFilePath, uxmlContent);
            }
            
            // Extract USS/CSS content
            string ussContent = ExtractContent("<CSS>", "</CSS>");
            string ussFilePath = "";
            
            if (!string.IsNullOrEmpty(ussContent))
            {
                ussFilePath = Path.Combine(resourcesPath, fileName + "Styles.uss");
                File.WriteAllText(ussFilePath, ussContent);
            }
            
            // Extract C# content
            string csharpContent = ExtractContent("<C#>", "</C#>");
            string csharpFilePath = "";
            
            // Somente salvar o script C# se a opção estiver ativada
            if (_generateScript && !string.IsNullOrEmpty(csharpContent))
            {
                csharpFilePath = Path.Combine(scriptsPath, fileName + "Controller.cs");
                File.WriteAllText(csharpFilePath, csharpContent);
            }
            
            // Salvar o nome da UI nas preferências
            EditorPrefs.SetString("BedrockUIGenerator_UIName", _uiName);
            
            AssetDatabase.Refresh();
            
            // Se a opção de usar a cena atual estiver ativada, procurar por UIDocument na cena
            if (_useCurrentScene)
            {
                // Aguardar a compilação do script antes de tentar anexar à cena
                EditorApplication.delayCall += () => {
                    AttachToUIDocumentInScene(uxmlFilePath, ussFilePath, _generateScript ? csharpFilePath : "", fileName);
                };
            }
            
            string successMessage = $"Files saved successfully in Resources folder:\n- {fileName}Layout.uxml\n- {fileName}Styles.uss";
            if (_generateScript)
                successMessage += $"\n- {fileName}Controller.cs";
            
            EditorUtility.DisplayDialog("Success", successMessage, "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error saving generated code: {ex.Message}");
            EditorUtility.DisplayDialog("Error", $"Failed to save files: {ex.Message}", "OK");
        }
    }

    private string ExtractContent(string startTag, string endTag)
    {
        int startIndex = _generatedCode.IndexOf(startTag);
        if (startIndex < 0) return string.Empty;
        
        startIndex += startTag.Length;
        int endIndex = _generatedCode.IndexOf(endTag, startIndex);
        if (endIndex < 0) return string.Empty;
        
        return _generatedCode.Substring(startIndex, endIndex - startIndex).Trim();
    }

    private string ExtractOrGenerateUXML()
    {
        // Implementação básica para tentar extrair UXML do código gerado
        // ou gerar um UXML básico se não for possível extrair
        
        StringBuilder uxmlBuilder = new StringBuilder();
        uxmlBuilder.AppendLine("<UXML xmlns=\"UnityEngine.UIElements\" xmlns:uie=\"UnityEditor.UIElements\">");
        uxmlBuilder.AppendLine("  <Style src=\"UIStyles.uss\" />");
        uxmlBuilder.AppendLine("  <VisualElement name=\"root\" class=\"container\">");
        
        // Tentar encontrar elementos no código
        if (_generatedCode.Contains("new Button"))
        {
            uxmlBuilder.AppendLine("    <Button text=\"Botão\" name=\"button\" class=\"button\" />");
        }
        
        if (_generatedCode.Contains("new Label"))
        {
            uxmlBuilder.AppendLine("    <Label text=\"Texto\" name=\"label\" class=\"label\" />");
        }
        
        if (_generatedCode.Contains("new TextField"))
        {
            uxmlBuilder.AppendLine("    <TextField name=\"textField\" class=\"text-field\" />");
        }
        
        uxmlBuilder.AppendLine("  </VisualElement>");
        uxmlBuilder.AppendLine("</UXML>");
        
        return uxmlBuilder.ToString();
    }

    [Obsolete]
    private void AttachToUIDocumentInScene(string uxmlPath, string ussPath, string scriptPath, string fileName)
    {
        try
        {
            // Procurar por UIDocument na cena atual
            UIDocument[] uiDocuments = FindObjectsOfType<UIDocument>();
            
            if (uiDocuments != null && uiDocuments.Length > 0)
            {
                // Carregar os assets recém-criados
                var uxmlAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath.Replace(Application.dataPath, "Assets"));
                var ussAsset = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath.Replace(Application.dataPath, "Assets"));

                // Usar o primeiro UIDocument encontrado
                UIDocument uiDocument = uiDocuments[0];
                
                // Atualizar o UIDocument com o novo UXML
                if (uxmlAsset != null)
                {
                    Undo.RecordObject(uiDocument, "Update UIDocument source");
                    uiDocument.visualTreeAsset = uxmlAsset;
                    EditorUtility.SetDirty(uiDocument);
                }
                
                // Adicionar o script controller ao GameObject do UIDocument
                if (_generateScript && !string.IsNullOrEmpty(scriptPath))
                {
                    // Esperar que o script seja compilado
                    EditorApplication.delayCall += () => {
                        try
                        {
                            // Obter o tipo do script recém-criado
                            var controllerType = GetTypeByName(fileName + "Controller");
                            
                            if (controllerType != null)
                            {
                                // Verificar se o componente já existe
                                if (uiDocument.GetComponent(controllerType) == null)
                                {
                                    // Adicionar o componente ao GameObject
                                    Undo.AddComponent(uiDocument.gameObject, controllerType);
                                    EditorUtility.SetDirty(uiDocument.gameObject);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Error attaching controller script: {ex.Message}");
                        }
                    };
                }
                
                Debug.Log($"UI attached to existing UIDocument in scene: {uiDocument.gameObject.name}");
            }
            else
            {
                // Se não encontrar UIDocument, criar um novo GameObject com UIDocument
                EditorApplication.delayCall += () => {
                    try
                    {
                        // Criar um novo GameObject
                        GameObject uiGameObject = new GameObject(fileName + "Document");
                        Undo.RegisterCreatedObjectUndo(uiGameObject, "Create UI GameObject");
                        
                        // Adicionar UIDocument
                        var uiDocument = Undo.AddComponent<UnityEngine.UIElements.UIDocument>(uiGameObject);
                        
                        // Carregar os assets recém-criados
                        var uxmlAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath.Replace(Application.dataPath, "Assets"));
                        var ussAsset = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath.Replace(Application.dataPath, "Assets"));
                        
                        // Configurar o UIDocument
                        if (uxmlAsset != null)
                        {
                            uiDocument.visualTreeAsset = uxmlAsset;
                        }
                        
                        if (ussAsset != null)
                        {
                            uiDocument.rootVisualElement.styleSheets.Add(ussAsset);
                        }
                        
                        // Adicionar o script controller
                        var controllerType = GetTypeByName(fileName + "Controller");
                        if (controllerType != null)
                        {
                            Undo.AddComponent(uiGameObject, controllerType);
                        }
                        
                        EditorUtility.SetDirty(uiGameObject);
                        Debug.Log($"Created new UIDocument GameObject: {uiGameObject.name}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error creating UIDocument: {ex.Message}");
                    }
                };
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error attaching to UIDocument: {ex.Message}");
        }
    }

    private Type GetTypeByName(string className)
    {
        // Try to find the type in all loaded assemblies
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // First try with exact name match
            var type = assembly.GetType(className);
            if (type != null)
                return type;
            
            // If not found, try to find by searching all types in the assembly
            foreach (var t in assembly.GetTypes())
            {
                // Check if the type name matches (without namespace)
                if (t.Name == className)
                    return t;
            }
        }
        
        Debug.LogWarning($"Could not find type: {className}. Make sure the script has been compiled.");
        return null;
    }
}