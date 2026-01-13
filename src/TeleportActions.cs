using UnityEngine;

namespace AethermancerHarness
{
    public static class TeleportActions
    {
        public static string ExecuteInstantTeleport(float x, float y, float z)
        {
            string error = null;
            Vector3 oldPos = Vector3.zero;

            Plugin.RunOnMainThreadAndWait(() =>
            {
                if (GameStateManager.Instance?.IsCombat ?? false)
                {
                    error = "Cannot teleport during combat";
                    return;
                }

                var playerMovement = PlayerMovementController.Instance;
                if (playerMovement == null)
                {
                    error = "PlayerMovementController not available";
                    return;
                }

                oldPos = playerMovement.transform.position;
                playerMovement.transform.position = new Vector3(x, y, z);
            });

            if (error != null)
                return JsonConfig.Error(error);

            return JsonConfig.Serialize(new {
                success = true,
                from = new { x = oldPos.x, y = oldPos.y, z = oldPos.z },
                to = new { x, y, z }
            });
        }

        public static string ExecuteAnimatedTeleport(float x, float y, float z)
        {
            string error = null;
            Vector3 oldPos = Vector3.zero;
            var targetPos = new Vector3(x, y, z);

            Plugin.RunOnMainThreadAndWait(() =>
            {
                if (GameStateManager.Instance?.IsCombat ?? false)
                {
                    error = "Cannot teleport during combat";
                    return;
                }

                var playerMovement = PlayerMovementController.Instance;
                if (playerMovement == null)
                {
                    error = "PlayerMovementController not available";
                    return;
                }

                oldPos = playerMovement.transform.position;
                playerMovement.TeleportToPOI(targetPos);
            });

            if (error != null)
                return JsonConfig.Error(error);

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
