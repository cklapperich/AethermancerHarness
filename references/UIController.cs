#region Assembly Assembly-CSharp, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// /home/klappec/.steam/debian-installation/steamapps/common/Aethermancer/Aethermancer_Data/Managed/Assembly-CSharp.dll
// Decompiled with ICSharpCode.Decompiler 
#endregion

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class UIController : MonoBehaviour
{
    public enum CombatUIStates
    {
        ShowAll,
        DisableSelectionMenus,
        DisableAll
    }

    public CombatUIController CombatUI;

    public PopupController PopupController;

    public MonsterMementoPopup MonsterMementoPopup;

    public TutorialPopupController TutorialPopupController;

    public PostCombatMenu PostCombatMenu;

    public MonsterSelectMenu MonsterSelectMenu;

    public DialogueDisplay DialogueDisplay;

    public MonsterShrineMenu MonsterShrineMenu;

    public CombatInspectorUI CombatInspectorUI;

    public DifficultySelectionMenu DifficultySelectMenu;

    public ExplorationMonsterInfoMenu MonsterInfo;

    public BigTextBanner BigTextBanner;

    public NicknameMenu NicknameMenu;

    public HoverTooltip HoverTooltip;

    [SerializeField]
    private AetherSpringMenu AetherSpringMenu;

    [SerializeField]
    private ExplorationHUD ExplorationHUD;

    public ShadeLayer ShadeLayer;

    public CheatMenu Cheats;

    public PlayerButtonPrompt PlayerButtonPrompt;

    [SerializeField]
    private PauseMenu PauseMenu;

    [SerializeField]
    private InventoryMenu InventoryMenu;

    [SerializeField]
    private ExplorationMapUIMenu Map;

    [SerializeField]
    private MerchantMenu MerchantMenu;

    [SerializeField]
    private SettingsMenu SettingsMenu;

    [SerializeField]
    private MetaUpgradeMenu MetaUpgradeMenu;

    public IngameBugReportMenu BugReportMenu;

    [SerializeField]
    private SpriteRenderer BlackScreen;

    [SerializeField]
    private EndOfRunMenu EndOfRunMenu;

    public CombatUIStates CurrentCombatUIState;

    public CombatUIStates CurrentExplorationUIState;

    public bool IsInSubmenu
    {
        get
        {
            if (!MonsterInfo.IsOpen && !Map.IsOpen && !SettingsMenu.IsOpen)
            {
                return InventoryMenu.IsOpen;
            }

            return true;
        }
    }

    public bool IsInMenu
    {
        get
        {
            if (!IsInSubmenu && !PostCombatMenu.IsOpen && !MonsterSelectMenu.IsOpen && !MonsterShrineMenu.IsOpen && !DifficultySelectMenu.IsOpen && !AetherSpringMenu.IsOpen && !PauseMenu.IsOpen && !MerchantMenu.IsOpen && !MetaUpgradeMenu.IsOpen && !DialogueDisplay.IsOpen && !BugReportMenu.IsOpen && !InventoryMenu.IsOpen && !NicknameMenu.IsOpen)
            {
                return EndOfRunMenu.IsOpen;
            }

            return true;
        }
    }

    public bool IsInPauseMenu => PauseMenu.IsOpen;

    public bool IsInMap => Map.IsOpen;

    public bool IsInBugReporting => BugReportMenu.IsOpen;

    public static UIController Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Object.Destroy(base.gameObject);
            return;
        }

        Instance = this;
        Initialize();
    }

    private void Initialize()
    {
        CombatUI.gameObject.SetActive(value: true);
        PopupController.gameObject.SetActive(value: true);
        TutorialPopupController.gameObject.SetActive(value: true);
        MonsterMementoPopup.gameObject.SetActive(value: true);
        PostCombatMenu.gameObject.SetActive(value: true);
        MonsterSelectMenu.gameObject.SetActive(value: true);
        MonsterShrineMenu.gameObject.SetActive(value: true);
        DifficultySelectMenu.gameObject.SetActive(value: true);
        AetherSpringMenu.gameObject.SetActive(value: true);
        Cheats.gameObject.SetActive(value: true);
        BigTextBanner.gameObject.SetActive(value: true);
        ShadeLayer.gameObject.SetActive(value: true);
        PlayerButtonPrompt.gameObject.SetActive(value: true);
        PauseMenu.gameObject.SetActive(value: true);
        CombatInspectorUI.gameObject.SetActive(value: true);
        Map.gameObject.SetActive(value: true);
        MonsterInfo.gameObject.SetActive(value: true);
        MerchantMenu.gameObject.SetActive(value: true);
        MetaUpgradeMenu.gameObject.SetActive(value: true);
        DialogueDisplay.gameObject.SetActive(value: true);
        ExplorationHUD.gameObject.SetActive(value: true);
        ExplorationHUD.UiParent.SetActive(value: true);
        SettingsMenu.gameObject.SetActive(value: true);
        BugReportMenu.gameObject.SetActive(value: true);
        InventoryMenu.gameObject.SetActive(value: true);
        HoverTooltip.gameObject.SetActive(value: true);
        NicknameMenu.gameObject.SetActive(value: true);
        EndOfRunMenu.gameObject.SetActive(value: true);
    }

    public void ShowDefaultUiForScene(ESceneType sceneType)
    {
        switch (sceneType)
        {
            case ESceneType.Combat:
                SetExplorationHUDVisibility(visible: false);
                SetCombatUIVisibility(visible: true);
                PlayerButtonPrompt.SetVoidBlitzAvailable(available: false);
                PlayerButtonPrompt.SetAetherParryAvailable(available: false);
                break;
            case ESceneType.Exploration:
            case ESceneType.RestSite:
            case ESceneType.Tutorial:
                SetCombatUIVisibility(visible: false);
                SetExplorationHUDVisibility(visible: true, isAfterSceneChange: true);
                break;
            case ESceneType.MainMenu:
            case ESceneType.PilgrimsRest:
                SetExplorationHUDVisibility(visible: true, isAfterSceneChange: false, forceOpen: true);
                SetCombatUIVisibility(visible: false);
                break;
        }
    }

    public void SetCombatUIVisibility(bool visible)
    {
        CombatUI.gameObject.SetActive(visible);
    }

    public void SetInventoryUIVisibility(bool visible, UnityAction callback = null)
    {
        if (visible)
        {
            if (IsInPauseMenu)
            {
                PauseMenu.Close(playSoundAndAnim: false);
            }

            InventoryMenu.Open(callback);
        }
        else
        {
            InventoryMenu.Close();
        }
    }

    public void SetExplorationHUDVisibility(bool visible, bool isAfterSceneChange = false, bool forceOpen = false, bool isAfterMonsterRebirthed = false)
    {
        if (visible && forceOpen)
        {
            ExplorationHUD.Open(isAfterSceneChange, isAfterMonsterRebirthed);
        }
        else if (visible && GameStateManager.Instance.IsExploringOrInterrupted && !GameStateManager.Instance.IsMenu && GameController.Instance.GameplayType != EGameplayType.CombatPrototype && SceneManager.GetActiveScene() != SceneManager.GetSceneByPath(SceneLoader.Instance.DemoScene.ScenePath))
        {
            ExplorationHUD.Open(isAfterSceneChange, isAfterMonsterRebirthed);
        }
        else
        {
            ExplorationHUD.Close();
        }
    }

    public void SetPauseMenuVisibility(bool visible)
    {
        if (visible)
        {
            PauseMenu.Open(playSoundAndAnim: true);
        }
        else
        {
            PauseMenu.Close();
        }

        SetExplorationHUDVisibility(!visible);
    }

    public void SetCombatInspectorVisibility(bool visible, List<Monster> Allies, List<Monster> Enemies, int monsterIndex = 0, UnityAction<int> onConfirm = null, UnityAction<int> onCancel = null, bool animateBackground = false, bool freezeTime = false)
    {
        if (visible)
        {
            CombatInspectorUI.SetMonsters(Allies, Enemies);
            CombatInspectorUI.Open(monsterIndex, onConfirm, onCancel, animateBackground, freezeTime);
        }
        else
        {
            MonsterInfo.Close();
        }
    }

    public void SetMonsterInfoVisibility(bool visible, List<Monster> monsters, ExplorationMonsterInfoMenu.EMonsterInfoState state, int monsterIndex = 0, UnityAction<int> onConfirm = null, UnityAction<int> onCancel = null, bool animateBackground = false, bool freezeTime = true)
    {
        if (visible)
        {
            if (PauseMenu.IsOpen)
            {
                PauseMenu.Close(playSoundAndAnim: false);
            }

            MonsterInfo.SetMonsters(monsters);
            MonsterInfo.Open(monsterIndex, state, onConfirm, onCancel, animateBackground, freezeTime);
        }
        else
        {
            MonsterInfo.Close();
        }
    }

    public void SetMapVisibility(bool visible, bool instantClose)
    {
        if (visible)
        {
            PauseMenu.Close(playSoundAndAnim: false);
            Map.Open(instantClose);
        }
        else
        {
            Map.Close();
            PauseMenu.Open();
        }
    }

    public void SetMapTextures(Texture2D mapTexture, Texture2D minimapTexture, Texture2D exploredTexture, int width, int height)
    {
        Map.SetMap(mapTexture, minimapTexture, exploredTexture, width, height);
    }

    public void SetMonsterShrineVisibility(bool visible, EShrineState shrineState = EShrineState.NormalShrineSelection, int index = 0, UnityAction reviveCallback = null, UnityAction cancelCallback = null, MonsterShrineTrigger activatingInteractable = null)
    {
        if (visible)
        {
            MonsterShrineMenu.Open(index, shrineState, reviveCallback, cancelCallback, activatingInteractable);
        }
        else
        {
            MonsterShrineMenu.Close();
        }

        if (GameController.Instance.GameplayType != EGameplayType.CombatPrototype && ExplorationController.Instance.CurrentArea == EArea.PilgrimsRest)
        {
            SetExplorationHUDVisibility(!visible, isAfterSceneChange: false, forceOpen: true);
        }
    }

    public void SetDifficultySelectVisibility(bool visible, DifficultySelectionMenu.DifficultySelectDelegate selectDelegate)
    {
        if (visible)
        {
            DifficultySelectMenu.Open(selectDelegate);
        }
        else
        {
            DifficultySelectMenu.Close();
        }
    }

    public void SetDialogueVisibility(bool visible, DialogueInteractable dialogue = null, UnityAction callback = null)
    {
        if (!visible || !(dialogue == null))
        {
            if (visible)
            {
                DialogueDisplay.Open(dialogue, callback);
            }
            else
            {
                DialogueDisplay.Close();
            }

            bool visible2 = ExplorationController.Instance.CurrentArea != EArea.Void && !visible;
            SetExplorationHUDVisibility(visible2);
        }
    }

    public void SetAetherSpringMenuVisibility(bool visible, AetherSpringInteractable aetherSpring = null, UnityAction callback = null)
    {
        if (!visible || !(aetherSpring == null))
        {
            if (visible)
            {
                AetherSpringMenu.Open(aetherSpring, callback);
            }
            else
            {
                AetherSpringMenu.Close();
            }

            SetExplorationHUDVisibility(!visible);
        }
    }

    public void SetMerchantMenuVisibility(bool visible, MerchantInteractable merchant = null, UnityAction callback = null)
    {
        if (!visible || !(merchant == null))
        {
            if (visible)
            {
                MerchantMenu.Open(merchant, callback);
            }
            else
            {
                MerchantMenu.Close();
            }

            SetExplorationHUDVisibility(!visible);
        }
    }

    public void SetMetaUpgradeMenuVisibility(bool visible, MetaUpgradeDialogueEventManager dialogue = null, UnityAction callback = null)
    {
        if (!visible || !(dialogue == null))
        {
            if (visible)
            {
                MetaUpgradeMenu.Open(dialogue, callback);
            }
            else
            {
                MetaUpgradeMenu.Close();
            }

            SetExplorationHUDVisibility(!visible);
        }
    }

    public void SetEquipmentSelectionVisibility(bool visible, MonsterSelectMenu.MonsterSelectMethod callback = null, EquipmentInstance equipment = null)
    {
        if (visible)
        {
            MonsterSelectMenu.Open(callback, MonsterSelectMenu.ESelectType.EquipmentSelect, equipment);
        }
        else
        {
            MonsterSelectMenu.Close();
        }

        SetExplorationHUDVisibility(!visible);
    }

    public bool GetMonsterShrineVisibility()
    {
        return MonsterShrineMenu.IsOpen;
    }

    public void SetSettingsMenuVisibility(bool visible, UnityAction callback = null)
    {
        if (visible)
        {
            if (IsInPauseMenu)
            {
                PauseMenu.Close(playSoundAndAnim: false);
            }

            SettingsMenu.Open(callback);
        }
        else
        {
            SettingsMenu.Close();
        }
    }

    public void SetBlackScreenVisibility(bool visible, float fadeDuration = 0f, UnityAction callback = null)
    {
        if (visible)
        {
            BlackScreen.gameObject.SetActive(value: true);
            if (fadeDuration <= 0f)
            {
                Color color = BlackScreen.color;
                color.a = 1f;
                BlackScreen.color = color;
            }
            else
            {
                StartCoroutine(FadeBlackScreen(0f, 1f, fadeDuration, callback));
            }
        }
        else if (fadeDuration <= 0f)
        {
            Color color2 = BlackScreen.color;
            color2.a = 0f;
            BlackScreen.color = color2;
            BlackScreen.gameObject.SetActive(value: false);
        }
        else
        {
            StartCoroutine(FadeBlackScreen(1f, 0f, fadeDuration, delegate
            {
                BlackScreen.gameObject.SetActive(value: false);
                callback?.Invoke();
            }));
        }
    }

    private IEnumerator FadeBlackScreen(float startAlpha, float endAlpha, float fadeDuration, UnityAction callback)
    {
        float timer = 0f;
        Color temp = BlackScreen.color;
        for (; timer < fadeDuration; timer += Time.deltaTime)
        {
            temp.a = Mathf.Lerp(startAlpha, endAlpha, timer / fadeDuration);
            BlackScreen.color = temp;
            yield return null;
        }

        temp.a = endAlpha;
        BlackScreen.color = temp;
        callback();
    }

    public void OpenEndOfRunMenu(GameData data, UnityAction onClose)
    {
        EndOfRunMenu.Open(data, onClose);
    }

    public void CloseEndofRun()
    {
        EndOfRunMenu.Close();
    }

    public void SetCombatUiState(CombatUIStates state)
    {
        CurrentCombatUIState = state;
        switch (state)
        {
            case CombatUIStates.ShowAll:
                SetCombatHUDCanBeShown(canShow: true);
                SetCombatSelectionMenuCanBeShown(canShow: true);
                break;
            case CombatUIStates.DisableSelectionMenus:
                SetCombatHUDCanBeShown(canShow: true);
                SetCombatSelectionMenuCanBeShown(canShow: false);
                break;
            case CombatUIStates.DisableAll:
                SetCombatHUDCanBeShown(canShow: false);
                SetCombatSelectionMenuCanBeShown(canShow: false);
                break;
        }
    }

    private void SetCombatHUDCanBeShown(bool canShow)
    {
        CombatUI.CanShow = canShow;
        CombatUI.TraitCounterViewPlayer.CanShow = canShow;
        CombatUI.TraitCounterViewEnemy.CanShow = canShow;
        foreach (PlayerMonsterHUD playerMonsterHUD in CombatUI.PlayerMonsterHUDs)
        {
            playerMonsterHUD.CanShow = canShow;
            playerMonsterHUD.BuffContainer.CanShow = canShow;
            if (playerMonsterHUD.Monster != null && playerMonsterHUD.Monster.TargetVisuals != null)
            {
                playerMonsterHUD.Monster.TargetVisuals.CanShow = canShow;
            }
        }

        foreach (Monster enemy in CombatController.Instance.Enemies)
        {
            enemy.EnemyMonsterHUD.SetVisibility(canShow);
            enemy.EnemyMonsterHUD.CanShow = canShow;
            if (enemy.TargetVisuals != null)
            {
                enemy.TargetVisuals.CanShow = canShow;
            }
        }
    }

    private void SetCombatSelectionMenuCanBeShown(bool canShow)
    {
        CombatUI.Menu.CanShow = canShow;
        CombatUI.ActionTitle.CanShow = canShow;
        PopupController.CanShow = canShow;
    }

    public void SetExplorationUiState(CombatUIStates state)
    {
        CurrentExplorationUIState = state;
        switch (state)
        {
            case CombatUIStates.ShowAll:
                PlayerButtonPrompt.CanShow = true;
                break;
            case CombatUIStates.DisableAll:
                PlayerButtonPrompt.CanShow = false;
                break;
        }
    }
}