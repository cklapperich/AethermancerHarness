using System;
using System.Collections.Generic;
using System.Linq;

namespace AethermancerHarness
{
    /// <summary>
    /// Validation helpers and utilities for consistent error handling across all action handlers.
    /// </summary>
    public static partial class ActionHandler
    {
        // =====================================================
        // COMBAT STATE VALIDATION
        // =====================================================

        /// <summary>
        /// Validates that the game is not in combat. Returns (true, null) if valid.
        /// </summary>
        internal static (bool isValid, string error) ValidateNotInCombat()
        {
            if (GameStateManager.Instance?.IsCombat ?? false)
                return (false, "Cannot perform this action during combat");
            return (true, null);
        }

        /// <summary>
        /// Validates that dialogue is not currently open.
        /// </summary>
        internal static (bool isValid, string error) ValidateDialogueClosed()
        {
            if (IsDialogueOpen())
                return (false, "Dialogue is already open");
            return (true, null);
        }

        // =====================================================
        // CHOICE INDEX VALIDATION
        // =====================================================

        /// <summary>
        /// Validates a choice index is within valid range [0, maxValid).
        /// Returns error string if invalid, null if valid.
        /// </summary>
        internal static string ValidateChoiceIndex(int choiceIndex, int maxValid, string contextName = "choice")
        {
            if (choiceIndex < 0 || choiceIndex >= maxValid)
            {
                string range = maxValid == 0 ? "no valid choices" : $"0-{maxValid - 1}";
                return JsonConfig.Error($"Invalid {contextName} index: {choiceIndex}. Valid range: {range}");
            }
            return null;
        }

        /// <summary>
        /// Validates choice index with inclusive upper bound (for cases like scrap as last item).
        /// Returns error string if invalid, null if valid.
        /// </summary>
        internal static string ValidateChoiceIndexInclusive(int choiceIndex, int maxValid, string contextName = "choice")
        {
            if (choiceIndex < 0 || choiceIndex > maxValid)
            {
                string range = maxValid == 0 ? "no valid choices" : $"0-{maxValid}";
                return JsonConfig.Error($"Invalid {contextName} index: {choiceIndex}. Valid range: {range}");
            }
            return null;
        }

        // =====================================================
        // GENERALIZED NAME FINDING HELPER
        // =====================================================

        /// <summary>
        /// Generic name finding helper: finds an object by name in a collection using a naming function.
        /// Example: FindByName("Merchant", interactables, x => x.Name)
        /// </summary>
        internal static (T obj, string error) FindByName<T>(
            string name,
            IEnumerable<T> collection,
            Func<T, string> getDisplayName,
            string collectionType = "item")
            where T : class
        {
            if (string.IsNullOrEmpty(name))
                return (null, $"{collectionType} name is required");

            var items = collection.ToList();
            if (items.Count == 0)
                return (null, $"No {collectionType}s available");

            // Find match (case-insensitive)
            foreach (var item in items)
            {
                var displayName = getDisplayName(item);
                if (!string.IsNullOrEmpty(displayName) &&
                    displayName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return (item, null);
                }
            }

            // Build available names for error message
            var available = items.Select(i => getDisplayName(i)).Where(n => !string.IsNullOrEmpty(n)).ToList();
            var availableStr = available.Count > 0
                ? string.Join(", ", available)
                : "(none)";

            return (null, $"No {collectionType} named '{name}'. Available: {availableStr}");
        }

        /// <summary>
        /// Validates a required string parameter.
        /// </summary>
        internal static string ValidateRequired(string value, string paramName)
        {
            if (string.IsNullOrEmpty(value))
                return JsonConfig.Error($"{paramName} is required");
            return null;
        }

        /// <summary>
        /// Validates that the game is in exploration mode.
        /// </summary>
        internal static (bool isValid, string error) ValidateExploring()
        {
            if (!(GameStateManager.Instance?.IsExploring ?? false))
                return (false, "Must be in exploration mode");
            return (true, null);
        }
    }
}
