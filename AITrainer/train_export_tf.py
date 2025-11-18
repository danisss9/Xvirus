import argparse
import os
import sys
import torch
import torch.nn as nn
import torch.optim as optim
from torchvision import datasets, transforms, models
from torchvision.models import MobileNet_V2_Weights
from torch.utils.data import random_split, DataLoader
from tqdm import tqdm

def main():
    parser = argparse.ArgumentParser(description="Train image classifier with MobileNetV2 and export to ONNX")
    parser.add_argument("--target", type=str, required=True, help="Path to dataset folder")
    parser.add_argument("--output", type=str, required=True, help="Path to save ONNX model")
    parser.add_argument("--epochs", type=int, default=10, help="Number of training epochs")
    parser.add_argument("--patience", type=int, default=5, help="Early stopping patience (epochs)")
    parser.add_argument("--batch-size", type=int, default=32, help="Batch size for training/validation")
    args = parser.parse_args()

    # Best-effort: ensure stdout can print Unicode on Windows (avoids emoji/encoding crashes)
    try:
        if hasattr(sys.stdout, "reconfigure"):
            sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass

    # Data transforms (resize to 224x224, normalize to ImageNet stats)
    # Grayscale images are converted to 3-channel automatically
    transform = transforms.Compose([
        transforms.Grayscale(num_output_channels=3),   # ensures grayscale → 3-channel RGB
        transforms.Resize((224, 224)),
        transforms.ToTensor(),
        transforms.Normalize(mean=[0.485, 0.456, 0.406],
                             std=[0.229, 0.224, 0.225])
    ])

    # Load dataset (folders = labels)
    dataset = datasets.ImageFolder(root=args.target, transform=transform)
    num_classes = len(dataset.classes)
    print(f"Detected {num_classes} classes: {dataset.classes}")

    # Train/Validation split (80/20) with reproducibility
    val_size = int(0.2 * len(dataset))
    train_size = len(dataset) - val_size
    generator = torch.Generator().manual_seed(42)
    train_dataset, val_dataset = random_split(dataset, [train_size, val_size], generator=generator)

    print(f"Train samples: {len(train_dataset)}, Val samples: {len(val_dataset)}")

    # DataLoaders with num_workers for faster loading
    train_loader = DataLoader(train_dataset, batch_size=args.batch_size, shuffle=True, num_workers=2)
    val_loader = DataLoader(val_dataset, batch_size=args.batch_size, shuffle=False, num_workers=2)

    # Load pretrained MobileNetV2 with modern weights API
    model = models.mobilenet_v2(weights=MobileNet_V2_Weights.DEFAULT)
    model.classifier[1] = nn.Linear(model.last_channel, num_classes)  # replace final layer

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    model = model.to(device)

    # Loss & optimizer
    criterion = nn.CrossEntropyLoss()
    optimizer = optim.Adam(model.parameters(), lr=0.001)

    # Scheduler (reduce LR when val loss plateaus)
    scheduler = optim.lr_scheduler.ReduceLROnPlateau(optimizer, mode="min", factor=0.5, patience=2)

    # Early stopping setup
    best_val_acc = 0.0
    patience_counter = 0
    best_model_state = model.state_dict()  # ensure always initialized

    # Training loop with validation + early stopping + LR scheduling
    for epoch in range(args.epochs):
        # ---- Training ----
        model.train()
        running_loss = 0.0
        correct_train, total_train = 0, 0

        for inputs, labels in tqdm(train_loader, desc=f"Epoch {epoch+1} Training"):
            inputs, labels = inputs.to(device), labels.to(device)

            optimizer.zero_grad()
            outputs = model(inputs)
            loss = criterion(outputs, labels)
            loss.backward()
            optimizer.step()

            running_loss += loss.item()
            _, predicted = torch.max(outputs, 1)
            correct_train += (predicted == labels).sum().item()
            total_train += labels.size(0)

        train_acc = 100 * correct_train / total_train

        # ---- Validation ----
        model.eval()
        correct_val, total_val = 0, 0
        val_loss = 0.0

        with torch.no_grad():
            for inputs, labels in tqdm(val_loader, desc=f"Epoch {epoch+1} Validation"):
                inputs, labels = inputs.to(device), labels.to(device)
                outputs = model(inputs)
                loss = criterion(outputs, labels)
                val_loss += loss.item()

                _, predicted = torch.max(outputs, 1)
                correct_val += (predicted == labels).sum().item()
                total_val += labels.size(0)

        val_acc = 100 * correct_val / total_val

        print(f"Epoch [{epoch+1}/{args.epochs}] "
              f"- Train Loss: {running_loss/len(train_loader):.4f}, Train Acc: {train_acc:.2f}% "
              f"- Val Loss: {val_loss/len(val_loader):.4f}, Val Acc: {val_acc:.2f}%")

        # ---- Scheduler step ----
        old_lr = optimizer.param_groups[0]['lr']
        scheduler.step(val_loss/len(val_loader))
        new_lr = optimizer.param_groups[0]['lr']
        if new_lr != old_lr:
            print(f"Learning rate reduced: {old_lr:.6f} → {new_lr:.6f}")

        # ---- Early stopping check ----
        if val_acc > best_val_acc:
            best_val_acc = val_acc
            patience_counter = 0
            best_model_state = model.state_dict()
        else:
            patience_counter += 1
            if patience_counter >= args.patience:
                print(f"Early stopping triggered at epoch {epoch+1}")
                break

    # Restore best model before export
    model.load_state_dict(best_model_state)

    # Save ONNX model as "model.ai"
    os.makedirs(args.output, exist_ok=True)
    onnx_path = os.path.join(args.output, "model.ai")

    dummy_input = torch.randn(1, 3, 224, 224, device=device)  # always 3-channel

    # Use legacy tracer: dynamo=False to avoid strict dynamic_shapes enforcement and emoji prints
    torch.onnx.export(
        model,
        dummy_input,
        onnx_path,
        input_names=["input"],
        output_names=["output"],
        dynamic_axes={"input": {0: "batch_size"}, "output": {0: "batch_size"}},
        opset_version=13,          # widely supported; 12/13/17 all fine; pick 13 for stability
        do_constant_folding=True,
        dynamo=False               # key fix: use legacy ONNX tracer
    )

    print(f"Best model exported to {onnx_path} with Val Acc: {best_val_acc:.2f}%")

if __name__ == "__main__":
    main()
