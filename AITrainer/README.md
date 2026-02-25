# AITrainer

Xvirus AI Model Trainer

## Table of Contents

- [AITrainer](#aitrainer)
  - [Table of Contents](#table-of-contents)
  - [Minimum Requirements](#minimum-requirements)
  - [Get Started](#get-started)
  - [Arguments](#arguments)
  - [How It Works](#how-it-works)
  - [Python Dependencies](#python-dependencies)

## Minimum Requirements

To use AITrainer you need:

- .NET 8 SDK - [download](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- Python 3.10+ - [download](https://www.python.org/downloads/)

## Get Started

Build and run the `AITrainer` project. All four arguments are required.

```bash
AITrainer --malware <path> --safe <path> --target <path> --output <path>
```

### Example

```bash
AITrainer --malware "C:\Samples\Malware" --safe "C:\Samples\Safe" --target "C:\Training\Images" --output "C:\Training\Model"
```

The output folder will contain the trained `model.ai` (ONNX) file ready to be placed in the SDK `Database` folder.

## Arguments

| Argument    | Required | Description                                                                          |
|-------------|----------|--------------------------------------------------------------------------------------|
| `--malware` | Yes      | Path to the folder containing malware files for training.                            |
| `--safe`    | Yes      | Path to the folder containing benign files for training.                             |
| `--target`  | Yes      | Path to the folder where training image files will be saved.                         |
| `--output`  | Yes      | Path to the folder where the final `model.ai` ONNX file will be saved.              |
| `--delete`  | No       | Set to `true` to delete the `--target` image folder after training is complete.      |

## How It Works

1. **Image conversion** — Each PE (executable) file from the `--malware` and `--safe` folders is converted to a grayscale PNG image. The binary content of each file is mapped directly to pixel values; the image width adapts to the file size to preserve structural patterns. Images are saved to `<target>/mal/` and `<target>/safe/` sub-folders, named by the file's MD5 hash.

2. **Model training** — The `train_export_tf.py` Python script is called automatically. It trains a MobileNetV2 image classifier (PyTorch) on the generated images with an 80/20 train/validation split, early stopping, and learning-rate scheduling.

3. **ONNX export** — The best checkpoint (by validation accuracy) is exported as `model.ai` (ONNX opset 13) to the `--output` folder.

4. **Cleanup** — If `--delete true` is passed, the `--target` image folder is deleted after training.

## Python Dependencies

Install the required packages before running:

```bash
pip install torch torchvision tqdm
```

GPU acceleration (CUDA) is used automatically if available; otherwise training runs on CPU.
