using UnityEngine;

namespace Axiom.Editor.AgentBridge.Core
{
    /// <summary>
    /// Represents the outcome of an AgentBridge action operation.
    /// </summary>
    public class ActionResult
    {
        public bool Success;
        public string Message;
        public Object Target;

        public static ActionResult Ok(string message, Object target = null)
            => new ActionResult { Success = true, Message = message, Target = target };

        public static ActionResult Fail(string message)
            => new ActionResult { Success = false, Message = message };
    }
}
