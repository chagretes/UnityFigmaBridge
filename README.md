
# Unity UI Toolkit Generator


This project is a fork of [Unity Figma Bridge](https://github.com/simonoliver/UnityFigmaBridge) with extended capabilities to enhance your Figma-to-Unity workflow.

## New Features

* **Multiple Import Configurations**: Manage multiple Figma document imports within a single project
* **Domain-Based Organization**: Organize assets in custom folders based on domain names
* **AI-Powered UI Toolkit Generation**: Create Unity UI Toolkit using AWS Bedrock
* **Incremental Updates**: Selectively update only changed elements (Work in Progress)

## Getting Started

Please refer to the [ORIGINAL_README.md](ORIGINAL_README.md) for basic setup and usage instructions from the original Unity Figma Bridge.

## Extended Features Guide

### Multiple Import Configurations

You can now create and manage multiple Figma document imports within the same Unity project:

1. Open Project Settings (Edit → Project Settings)
2. Navigate to the Unity Figma Bridge section
3. Click "Create" to create a new settings asset for each Figma document you want to import

### Domain-Based Organization

Organize your Figma assets in custom folders:

1. In your import configuration, set the "Domain" field to create a subfolder structure
2. Assets will be organized in `Assets/Figma/{domain}/...` instead of the default `Assets/Figma/...`

### AI-Powered UI Toolkit Generation

Convert your Figma designs to Unity UI Toolkit using AWS Bedrock:

1. Configure AWS Bedrock credentials in Window → AWS Bedrock → UI Generator
2. Set your AWS Access Key, Secret Key, and preferred region
3. During Figma import, select the option to generate UI Toolkit code

### Incremental Updates (WIP)

Update only changed elements in your Figma document:

1. Use the "Update Document (Incremental)" button in the settings inspector
2. This will attempt to update only modified elements while preserving existing assets

## Dependencies

* TextMeshPro 2.0.1
* JSON.Net 2.01
* AWS SDK for Bedrock (included)

## Feedback and Contributions

Contributions are welcome! Feel free to submit issues or pull requests.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Credits

This project builds upon the excellent work of [Simon Oliver](https://github.com/simonoliver) and the original Unity Figma Bridge contributors.

Additional AWS Bedrock integration developed to enhance the original functionality.