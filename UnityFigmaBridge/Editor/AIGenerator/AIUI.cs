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
            
            if (GUILayout.Button("Salvar como .cs"))
            {
                SaveGeneratedCode();
            }
            
            if (GUILayout.Button("Criar Arquivo UXML"))
            {
                SaveAsUXML();
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
        string path = EditorUtility.SaveFilePanel("Salvar Código C#", Application.dataPath, "UIGenerator", "cs");
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllText(path, _generatedCode);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Sucesso", "Arquivo salvo com sucesso!", "OK");
        }
    }

    private void SaveAsUXML()
    {
        try
        {
            // Extrair o UXML do código gerado (assumindo que está em comentários)
            int startIndex = _generatedCode.IndexOf("<!-- UXML");
            int endIndex = _generatedCode.IndexOf("-->", startIndex);
            
            string uxmlContent;
            
            if (startIndex >= 0 && endIndex >= 0)
            {
                uxmlContent = _generatedCode.Substring(startIndex, endIndex - startIndex + 3);
            }
            else
            {
                // Se não encontrar UXML em comentário, tentar gerar um básico
                uxmlContent = ExtractOrGenerateUXML();
            }
            
            string path = EditorUtility.SaveFilePanel("Salvar UXML", Application.dataPath, "UILayout", "uxml");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, uxmlContent);
                
                // Se o caminho estiver dentro do projeto, importar o asset
                if (path.StartsWith(Application.dataPath))
                {
                    AssetDatabase.Refresh();
                    EditorUtility.DisplayDialog("Sucesso", "Arquivo UXML salvo com sucesso!", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Erro ao salvar UXML: {ex.Message}");
            EditorUtility.DisplayDialog("Erro", $"Falha ao salvar UXML: {ex.Message}", "OK");
        }
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