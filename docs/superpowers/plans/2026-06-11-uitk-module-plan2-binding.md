# UITKModule Plan 2: Binding + MVVM + ListView + Source Generator

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add Source Generator auto-binding, MVVM data binding, and UITKListView to the UITKModule core (Plan 1).

**Architecture:** Roslyn Source Generator scans [Q]/[OnClick]/[OnChange]/[Bind]/[BindCommand] attributes, parses corresponding UXML files for compile-time validation, and generates binding/unbinding code. MVVM uses BindableProperty/Command/List with generated subscription wiring. UITKListView wraps Unity ListView with Widget lifecycle.

**Tech Stack:** Roslyn Source Generator (netstandard2.0), Unity 2022.3 UI Toolkit, UniTask

**Spec Reference:** `docs/superpowers/specs/2026-06-11-uitk-module-design.md` sections 5-8

**Depends On:** Plan 1 (Core Framework) fully implemented

---

## File Map

| Action | Path | Responsibility |
|--------|------|---------------|
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/QAttribute.cs` | [Q] element query attribute |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/OnClickAttribute.cs` | [OnClick] click event attribute |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/OnChangeAttribute.cs` | [OnChange] value change attribute |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/BindAttribute.cs` | [Bind] MVVM data binding attribute |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/BindCommandAttribute.cs` | [BindCommand] command binding attribute |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/BindingMode.cs` | OneWay/TwoWay/OneWayToSource enum |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/MVVM/BindableProperty.cs` | Reactive property with change notification |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/MVVM/BindableCommand.cs` | Command with CanExecute support |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/MVVM/BindableList.cs` | Observable collection |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/MVVM/ViewModelBase.cs` | ViewModel base class |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/MVVM/IValueConverter.cs` | Type converter interface + built-ins |
| Create | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/ListView/UITKListView.cs` | Virtualized list wrapping Unity ListView |
| Create | `Packages/UITKSourceGenerator/UITKSourceGenerator.csproj` | Source Generator project |
| Create | `Packages/UITKSourceGenerator/NamingConventions.cs` | camelCase → kebab-case conversion |
| Create | `Packages/UITKSourceGenerator/UXMLParser.cs` | Lightweight UXML XML parser |
| Create | `Packages/UITKSourceGenerator/DiagnosticDescriptors.cs` | Compile error definitions |
| Create | `Packages/UITKSourceGenerator/UITKBindingGenerator.cs` | Main generator: [Q], events, MVVM |
| Modify | `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UITKBase.cs` | Add BindContext/UnbindContext methods |
| Modify | `Assets/GameScripts/HotFix/GameLogic/GameLogic.asmdef` | Add analyzer reference to Source Generator |

---

## Task 1: Binding Attributes

**Files:**
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/QAttribute.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/OnClickAttribute.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/OnChangeAttribute.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/BindAttribute.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/BindCommandAttribute.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/BindingMode.cs`

- [ ] **Step 1: Create all attribute files**

QAttribute.cs:
```csharp
using System;
namespace GameLogic
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class QAttribute : Attribute
    {
        public string Name { get; }
        public QAttribute() { }
        public QAttribute(string name) => Name = name;
    }
}
```

OnClickAttribute.cs:
```csharp
using System;
namespace GameLogic
{
    [AttributeUsage(AttributeTargets.Method)]
    public class OnClickAttribute : Attribute
    {
        public string Target { get; }
        public OnClickAttribute() { }
        public OnClickAttribute(string target) => Target = target;
    }
}
```

OnChangeAttribute.cs:
```csharp
using System;
namespace GameLogic
{
    [AttributeUsage(AttributeTargets.Method)]
    public class OnChangeAttribute : Attribute
    {
        public string Target { get; }
        public OnChangeAttribute(string target) => Target = target;
    }
}
```

BindAttribute.cs:
```csharp
using System;
namespace GameLogic
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class BindAttribute : Attribute
    {
        public string Path { get; }
        public BindingMode Mode { get; }
        public string Format { get; }
        public Type Converter { get; }
        public BindAttribute(string path, BindingMode mode = BindingMode.OneWay, string format = null, Type converter = null)
        { Path = path; Mode = mode; Format = format; Converter = converter; }
    }
}
```

BindCommandAttribute.cs:
```csharp
using System;
namespace GameLogic
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class BindCommandAttribute : Attribute
    {
        public string CommandName { get; }
        public BindCommandAttribute(string commandName) => CommandName = commandName;
    }
}
```

BindingMode.cs:
```csharp
namespace GameLogic
{
    public enum BindingMode { OneWay, TwoWay, OneWayToSource }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Binding/
git commit -m "feat(uitk): add binding attributes"
```

---

## Task 2: MVVM Core

**Files:**
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/MVVM/BindableProperty.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/MVVM/BindableCommand.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/MVVM/BindableList.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/MVVM/ViewModelBase.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/MVVM/IValueConverter.cs`

- [ ] **Step 1: Create BindableProperty.cs**

```csharp
using System;
using System.Collections.Generic;
namespace GameLogic
{
    public class BindableProperty<T>
    {
        private T _value;
        public event Action<T> OnValueChanged;
        public T Value
        {
            get => _value;
            set { if (!EqualityComparer<T>.Default.Equals(_value, value)) { _value = value; OnValueChanged?.Invoke(_value); } }
        }
        public BindableProperty() { }
        public BindableProperty(T initial) => _value = initial;
        public static implicit operator T(BindableProperty<T> prop) => prop.Value;
    }
}
```

- [ ] **Step 2: Create BindableCommand.cs**

```csharp
using System;
namespace GameLogic
{
    public class BindableCommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;
        public event Action CanExecuteChanged;
        public BindableCommand(Action execute, Func<bool> canExecute = null) { _execute = execute; _canExecute = canExecute; }
        public bool CanExecute() => _canExecute?.Invoke() ?? true;
        public void Execute() { if (CanExecute()) _execute(); }
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke();
    }
}
```

- [ ] **Step 3: Create BindableList.cs**

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
namespace GameLogic
{
    public class BindableList<T> : IList<T>
    {
        private readonly List<T> _list = new();
        public event Action OnListChanged;
        public event Action<int, T> OnItemAdded;
        public event Action<int, T> OnItemRemoved;
        public event Action<int, T, T> OnItemChanged;
        internal IList InternalList => _list;
        public int Count => _list.Count;
        public bool IsReadOnly => false;
        public T this[int index] { get => _list[index]; set { T old = _list[index]; _list[index] = value; OnItemChanged?.Invoke(index, old, value); OnListChanged?.Invoke(); } }
        public void Add(T item) { _list.Add(item); OnItemAdded?.Invoke(_list.Count-1, item); OnListChanged?.Invoke(); }
        public void AddRange(IEnumerable<T> items) { foreach(var i in items) _list.Add(i); OnListChanged?.Invoke(); }
        public void Insert(int index, T item) { _list.Insert(index, item); OnItemAdded?.Invoke(index, item); OnListChanged?.Invoke(); }
        public bool Remove(T item) { int idx = _list.IndexOf(item); if(idx<0) return false; RemoveAt(idx); return true; }
        public void RemoveAt(int index) { T item = _list[index]; _list.RemoveAt(index); OnItemRemoved?.Invoke(index, item); OnListChanged?.Invoke(); }
        public void Clear() { _list.Clear(); OnListChanged?.Invoke(); }
        public bool Contains(T item) => _list.Contains(item);
        public int IndexOf(T item) => _list.IndexOf(item);
        public void CopyTo(T[] array, int i) => _list.CopyTo(array, i);
        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
```

- [ ] **Step 4: Create ViewModelBase.cs & IValueConverter.cs**

ViewModelBase.cs:
```csharp
namespace GameLogic
{
    public abstract class ViewModelBase { public virtual void Dispose() { } }
}
```

IValueConverter.cs:
```csharp
using UnityEngine.UIElements;
namespace GameLogic
{
    public interface IValueConverter<TSource, TTarget>
    {
        TTarget Convert(TSource value);
        TSource ConvertBack(TTarget value);
    }
    public class IntToStringConverter : IValueConverter<int, string>
    {
        public string Convert(int value) => value.ToString();
        public int ConvertBack(string value) => int.TryParse(value, out int r) ? r : 0;
    }
    public class FloatToStringConverter : IValueConverter<float, string>
    {
        public string Convert(float value) => value.ToString("F1");
        public float ConvertBack(string value) => float.TryParse(value, out float r) ? r : 0f;
    }
    public class BoolToDisplayConverter : IValueConverter<bool, DisplayStyle>
    {
        public DisplayStyle Convert(bool value) => value ? DisplayStyle.Flex : DisplayStyle.None;
        public bool ConvertBack(DisplayStyle value) => value == DisplayStyle.Flex;
    }
}
```

- [ ] **Step 5: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/MVVM/
git commit -m "feat(uitk): add MVVM core - BindableProperty, Command, List, ViewModelBase, converters"
```

---

## Task 3: UITKListView

**Files:**
- Create: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/ListView/UITKListView.cs`

- [ ] **Step 1: Create UITKListView.cs** (see spec design section 8 for full code)

Key implementation: wraps Unity `ListView`, `makeItem` creates Widget via UXML load + pool, `bindItem`/`unbindItem` calls `OnBindData`/`OnUnbindData`, supports `BindList<T>(BindableList<T>)` for auto-refresh.

- [ ] **Step 2: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/ListView/
git commit -m "feat(uitk): add UITKListView with virtualization and Widget lifecycle"
```

---

## Task 4: Add BindContext/UnbindContext to UITKBase

**Files:**
- Modify: `Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UITKBase.cs`

- [ ] **Step 1: Add MVVM support methods**

Add to UITKBase:
```csharp
private List<Action> _unbindActions;
protected void BindContext(ViewModelBase vm) { _unbindActions ??= new(); __UITKAutoBindViewModel(vm); }
protected void UnbindContext() { __UITKAutoUnbindViewModel(); }
protected virtual void __UITKAutoBindViewModel(ViewModelBase vm) { }
protected virtual void __UITKAutoUnbindViewModel() { if (_unbindActions != null) { foreach (var a in _unbindActions) a(); _unbindActions.Clear(); } }
protected void RegisterUnbindAction(Action action) { _unbindActions ??= new(); _unbindActions.Add(action); }
```

- [ ] **Step 2: Commit**

```bash
git add Assets/GameScripts/HotFix/GameLogic/Module/UITKModule/Base/UITKBase.cs
git commit -m "feat(uitk): add BindContext/UnbindContext to UITKBase"
```

---

## Task 5: Source Generator Project

**Files:**
- Create: `Packages/UITKSourceGenerator/UITKSourceGenerator.csproj`
- Create: `Packages/UITKSourceGenerator/NamingConventions.cs`
- Create: `Packages/UITKSourceGenerator/DiagnosticDescriptors.cs`
- Create: `Packages/UITKSourceGenerator/UXMLParser.cs`
- Create: `Packages/UITKSourceGenerator/UITKBindingGenerator.cs`

- [ ] **Step 1: Create project** (see spec design section 12 for full details)

Key: netstandard2.0 + Microsoft.CodeAnalysis.CSharp 4.3.0. NamingConventions handles camelCase→kebab-case. UXMLParser reads XML for name+type. UITKBindingGenerator is the main IIncrementalGenerator scanning [Q]/[OnClick]/[OnChange]/[Bind]/[BindCommand] and generating partial class methods.

- [ ] **Step 2: Build and copy DLL**

```bash
cd Packages/UITKSourceGenerator && dotnet build -c Release
cp bin/Release/netstandard2.0/UITKSourceGenerator.dll ../../Assets/Plugins/UITKSourceGenerator/
```

- [ ] **Step 3: Configure in Unity** — set DLL as RoslynAnalyzer, add UXML as AdditionalFiles

- [ ] **Step 4: Commit**

```bash
git add Packages/UITKSourceGenerator/ Assets/Plugins/UITKSourceGenerator/
git commit -m "feat(uitk): add Source Generator with compile-time UXML validation"
```

---

## Task 6: Integration Test

**Files:**
- Create: `Assets/AssetRaw/UITK/Shared/TestBindingWindow.uxml`
- Create: `Assets/GameScripts/HotFix/GameLogic/UI/TestUITK/TestBindingWindow.cs`
- Create: `Assets/GameScripts/HotFix/GameLogic/UI/TestUITK/TestBindingViewModel.cs`

- [ ] **Step 1: Create UXML + ViewModel + Window** (see spec design for full code)

- [ ] **Step 2: Test** — Show window, verify counter increment, TwoWay name binding, close animation

- [ ] **Step 3: Commit**

```bash
git add Assets/AssetRaw/UITK/Shared/TestBindingWindow.uxml Assets/GameScripts/HotFix/GameLogic/UI/TestUITK/TestBinding*
git commit -m "test(uitk): add integration test for auto-binding + MVVM"
```

---

## Summary

Plan 2 delivers:
- ✅ All binding attributes ([Q], [OnClick], [OnChange], [Bind], [BindCommand])
- ✅ MVVM core (BindableProperty, BindableCommand, BindableList, ViewModelBase)
- ✅ Built-in type converters
- ✅ UITKListView with virtualization + Widget lifecycle
- ✅ BindContext/UnbindContext on UITKBase
- ✅ Roslyn Source Generator with compile-time UXML validation
- ✅ Integration test demonstrating full auto-binding + MVVM + TwoWay
