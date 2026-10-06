# Totthodhara - Build & Compile Guide

**CRITICAL WARNING FOR FUTURE DEVELOPMENT:** Do not blindly compile this app using standard `.NET 8` minimization flags. If you do, you will break the WPF layout, the UI will crash upon opening, or the system graphics drivers will fail.

Follow this exact guide to produce a functioning, highly compressed, and Portable EXE.

## 1. Icon Preparation (Fixing the Stretched Logo)
A `.png` file cannot simply be renamed to `.ico`. Because `.ico` mandates a perfect square (e.g., 256x256), shoving a rectangular image into it causes severe horizontal stretching.

**To Generate the Icon:**
1. Use Python and the `Pillow` library to algorithmically pad the image.
2. Run this inside the root directory where `app.png` is stored:
```bash
python -c "from PIL import Image; img = Image.open('app.png').convert('RGBA'); s = max(img.size); new_img = Image.new('RGBA', (s, s), (0,0,0,0)); new_img.paste(img, ((s - img.size[0]) // 2, (s - img.size[1]) // 2)); new_img.save('app.ico', sizes=[(256, 256), (128, 128), (64, 64), (32, 32), (16, 16)])"
```

## 2. Preventing The App Crash (The Trimming Bug)
WPF uses extensive "Reflection" to dynamically search inside the code for XAML names and elements during `InitializeComponent()`. 
If you set `<PublishTrimmed>true</PublishTrimmed>`, the .NET compiler assumes these UI classes are "unused dead code" and forcibly deletes them to save file size. 
**Result:** Double-clicking the `.exe` will crash silently instantly.

**Rule:** NEVER use Trimming for WPF. 

## 3. Creating the Clean Portable Layout (Optimal Setup)
Because removing Trimming makes the file incredibly large (160+ MB), you must invoke "Single-File Compression". 
Furthermore, do not attempt to "hide" native WPF graphical DLLs (like `wpfgfx_cor3.dll`) inside an `assets/` folder by using `SetDllDirectory()`. This will break the deep `.NET Bootstrapper`.

**Your Totthodhara.csproj` should strictly look like this:**
```xml
    <RuntimeIdentifier>win-x86</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <PublishReadyToRun>true</PublishReadyToRun>
    
    <!-- DO NOT enable PublishTrimmed -->

    <!-- Force internal compression to shrink from 160MB to ~70MB -->
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    
    <!-- DO NOT enable IncludeNativeLibrariesForSelfExtract, as it pushes core files to AppData Temp unexpectedly in WPF -->
    
    <ApplicationIcon>app.ico</ApplicationIcon>
```

## 4. Final Compile Command
Whenever you update code and wish to produce the portable version, execute:

**For Universal Compatibility (x86/x64):**
```bash
dotnet publish Totthodhara.csproj -c Release -r win-x86 --self-contained true -o Totthodhara_Portable_Ultimate
```

**For Maximum Performance (x64 only):**
```bash
dotnet publish Totthodhara.csproj -c Release -r win-x64 --self-contained true -o Totthodhara_x64_Turbo
```

The output folder will beautifully just contain `Totthodhara.exe` and roughly 6 required base driver DLLs alongside it. It compresses the other ~140+ libraries securely into the `.exe`.
