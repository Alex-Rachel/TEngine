using System;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditorInternal;

namespace MCPForUnity.Editor.Resources.Project
{
    /// <summary>
    /// Provides list of all tags in the project.
    /// </summary>
    [McpForUnityResource("get_tags")]
    public static class Tags
    {
        public static object HandleCommand(JObject @params)
        {
            try
            {
                string[] tags = InternalEditorUtility.tags;
                return new SuccessResponse("Retrieved current tags.", tags);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Failed to retrieve tags: {e.Message}");
            }
        }
    }
}
