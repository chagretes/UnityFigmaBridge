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

public class BedrockUIGenerator : EditorWindow
{
    private string _prompt = "Crie um layout de menu principal para um jogo com botões para Jogar, Opções e Sair";
    private string _generatedCode = "";
    private string _modelId = "anthropic.claude-v2"; // Modelo padrão
    private bool _isGenerating = false;
    private string _awsAccessKey = "";
    private string _awsSecretKey = "";
    private string _awsRegion = "us-east-1";

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
            var credentials = new BasicAWSCredentials(_awsAccessKey, _awsSecretKey);
            var regionEndpoint = RegionEndpoint.GetBySystemName(_awsRegion);
            var client = new AmazonBedrockRuntimeClient(credentials, regionEndpoint);
            
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

            // Criar a requisição usando a API Converse
            var request = new ConverseRequest
            {
                ModelId = _modelId,
                Messages = new List<Message>
                {
                    new Message
                    {
                        Role = ConversationRole.User,
                        Content = new List<ContentBlock> { new ContentBlock { Text = promptText } }
                    }
                },
                InferenceConfig = new InferenceConfiguration
                {
                    MaxTokens = 5000,
                    Temperature = 0.7F,
                    TopP = 0.9F
                }
            };
            
            // Enviar a requisição e aguardar a resposta
            var response = await client.ConverseAsync(request);
            
            // Extrair o texto da resposta
            _generatedCode = response?.Output?.Message?.Content?[0]?.Text ?? "Nenhuma resposta recebida.";
            
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
            
            // Extract UXML content
            string uxmlContent = ExtractContent("<UXML>", "</UXML>");
            if (!string.IsNullOrEmpty(uxmlContent))
            {
                string uxmlFileName = EditorUtility.SaveFilePanel("Save UXML File", resourcesPath, "UILayout", "uxml");
                if (!string.IsNullOrEmpty(uxmlFileName))
                {
                    File.WriteAllText(uxmlFileName, uxmlContent);
                }
            }
            
            // Extract USS/CSS content
            string ussContent = ExtractContent("<CSS>", "</CSS>");
            if (!string.IsNullOrEmpty(ussContent))
            {
                string ussFileName = EditorUtility.SaveFilePanel("Save USS File", resourcesPath, "UIStyles", "uss");
                if (!string.IsNullOrEmpty(ussFileName))
                {
                    File.WriteAllText(ussFileName, ussContent);
                }
            }
            
            // Extract C# content
            string csharpContent = ExtractContent("<C#>", "</C#>");
            if (!string.IsNullOrEmpty(csharpContent))
            {
                string csharpFileName = EditorUtility.SaveFilePanel("Save C# Script", scriptsPath, "UIController", "cs");
                if (!string.IsNullOrEmpty(csharpFileName))
                {
                    File.WriteAllText(csharpFileName, csharpContent);
                }
            }
            
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Success", "Files saved successfully!", "OK");
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
}