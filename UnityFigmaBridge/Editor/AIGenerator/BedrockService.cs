using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using UnityEngine;

namespace UnityFigmaBridge.Editor.AIGenerator
{
    public class BedrockService
    {
        private string _awsAccessKey;
        private string _awsSecretKey;
        private string _awsRegion;
        private string _modelId;
        
        public BedrockService(string accessKey, string secretKey, string region, string modelId)
        {
            _awsAccessKey = accessKey;
            _awsSecretKey = secretKey;
            _awsRegion = region;
            _modelId = modelId;
        }
        
        public async Task<string> GenerateContent(string promptText)
        {
            try
            {
                var credentials = new BasicAWSCredentials(_awsAccessKey, _awsSecretKey);
                var regionEndpoint = RegionEndpoint.GetBySystemName(_awsRegion);
                var client = new AmazonBedrockRuntimeClient(credentials, regionEndpoint);
                
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
                return response?.Output?.Message?.Content?[0]?.Text ?? "Nenhuma resposta recebida.";
            }
            catch (Exception ex)
            {
                Debug.LogError($"Erro ao chamar AWS Bedrock: {ex.Message}");
                throw;
            }
        }
    }
} 