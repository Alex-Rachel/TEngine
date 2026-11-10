using Unity.Pipeline.Commands;

namespace Unity.Pipeline.Editor.Commands
{
    /// <summary>
    /// Quits the Editor without a save prompt. Unsaved scenes or an unsaved prefab stage fail the
    /// command unless <c>save</c> or <c>discard</c> says what to do with them.
    /// </summary>
    static class EditorQuitCommand
    {
        const string Group = "editor";

        [CliGroupCommand(Group, "Quit the Editor. Fails if scenes or a prefab have unsaved changes, unless --save or --discard is passed.", MainThreadRequired = true, Tags = new[] { "editor" })]
        public static EditorQuitResult Quit(
            [CliArg("save", "Save every dirty open scene and the open prefab before quitting. Fails if a dirty scene has never been saved.")] bool save = false,
            [CliArg("discard", "Quit without saving unsaved scenes or prefab changes.")] bool discard = false,
            [CliArg("exit-code", "Exit code for the Editor process")] int exitCode = 0)
        {
            return EditorQuit.Quit(save, discard, exitCode);
        }
    }
}
