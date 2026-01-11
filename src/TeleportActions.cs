using UnityEngine;

namespace AethermancerHarness
{
    public static class TeleportActions
    {
        public static string ExecuteInstantTeleport(float x, float y, float z)
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonConfig.Error("Cannot teleport during combat");

            var playerMovement = PlayerMovementController.Instance;
            if (playerMovement == null)
                return JsonConfig.Error("PlayerMovementController not available");

            var oldPos = playerMovement.transform.position;
            playerMovement.transform.position = new Vector3(x, y, z);

            return JsonConfig.Serialize(new {
                success = true,
                from = new { x = oldPos.x, y = oldPos.y, z = oldPos.z },
                to = new { x, y, z }
            });
        }

        public static string ExecuteAnimatedTeleport(float x, float y, float z)
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return JsonConfig.Error("Cannot teleport during combat");

            var playerMovement = PlayerMovementController.Instance;
            if (playerMovement == null)
                return JsonConfig.Error("PlayerMovementController not available");

            var oldPos = playerMovement.transform.position;
            var targetPos = new Vector3(x, y, z);

            Plugin.RunOnMainThreadAndWait(() =>
            {
                playerMovement.TeleportToPOI(targetPos);
            });

            // Wait for animation using ReadinessActions (HTTP thread - safe)
            ActionHandler.WaitUntilReady(5000);

            return JsonConfig.Serialize(new {
                success = true,
                animated = true,
                from = new { x = oldPos.x, y = oldPos.y, z = oldPos.z },
                to = new { x, y, z }
            });
        }

        public static string ExecuteTeleport(float x, float y, float z)
        {
            return Plugin.WatchableMode
                ? ExecuteAnimatedTeleport(x, y, z)
                : ExecuteInstantTeleport(x, y, z);
        }

        /// <summary>
        /// Internal helper: teleport without returning result (for use in interaction flows).
        /// MUST be called from main thread. Does NOT wait for animation - caller should use
        /// WaitUntilReady() on HTTP thread if waiting is needed.
        /// </summary>
        public static void TeleportInternal(Vector3 targetPos)
        {
            var playerMovement = PlayerMovementController.Instance;
            if (playerMovement == null)
                return;

            if (Plugin.WatchableMode)
            {
                playerMovement.TeleportToPOI(targetPos);
                // No waiting here - caller uses WaitUntilReady() on HTTP thread
            }
            else
            {
                playerMovement.transform.position = targetPos;
            }
        }
    }
}
