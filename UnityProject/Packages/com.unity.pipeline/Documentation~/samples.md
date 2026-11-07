---
uid: samples
---

# Import the Pipeline samples

Add the Unity Pipeline samples package, and import a sample into your project.

The Pipeline samples ship in a separate package, `com.unity.pipeline.samples`, so that `com.unity.pipeline` contains only the code that runs in your project. Import the Pipeline samples to view the **Method Reload Examples** sample, which contains two scenes that demonstrate [`[MethodReload]`](method-reload.md) method editing: `MethodReload_InPlace` and `MethodReload_Minics`.

## Add the Pipeline samples package

To add the Pipeline samples package:

1. Navigate to Unity’s main menu and select **Window** > **Package Management** > **Package Manager**.
1. Select **+** > **Install package by name**, enter `com.unity.pipeline.samples`, and select **Install**. For more information, refer to [Install a package by name](https://docs.unity3d.com/Manual/upm-ui-quick.html).

The `com.unity.pipeline.samples` depends on `com.unity.pipeline`, so the Package Manager installs `com.unity.pipeline` if your project doesn't already have it.

## Import Pipeline samples

When you add the package, the sample isn't added to your project. To import a sample:

1. Select **Unity Pipeline Samples** from the **Package Manager** > **All Packages** window.
1. Select the **Samples** tab, and select **Import** to import your chosen sample.

Unity copies the sample into your project's `Assets/Samples` folder, where you can open and edit its scenes.

## Additional resources

- [Reloading code](method-reload.md)
- [Runtime connection & setup](runtime-setup.md)
- [Install a package by name](https://docs.unity3d.com/Manual/upm-ui-quick.html)