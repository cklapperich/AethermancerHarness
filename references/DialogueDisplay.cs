using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class DialogueDisplay : MonoBehaviour
{
	[Header("References - Self")]
	public GameObject UiParent;

	public DialogueCharacterDisplay LeftCharacterDisplay;

	public DialogueCharacterDisplay RightCharacterDisplay;

	public SpriteRenderer BackgroundGradient;

	public MenuInputReminder InputReminder;

	public MenuListItem InputDescriptionProxy;

	[Header("Settings")]
	public float OpenDuration = 0.2f;

	public float CloseDuration = 0.2f;

	public ButtonViewSettings ViewMonstersDescription = new ButtonViewSettings(EInputType.Interact, EInputDisplayOverride.None, "View Monsters");

	public ButtonViewSettings GoBackDescription = new ButtonViewSettings(EInputType.Cancel, EInputDisplayOverride.None, "Go Back");

	[Header("Events")]
	public UnityEvent OnClose;

	private DialogueInteractable currentDialogue;

	private DialogueDisplayData currentDialogueData;

	private bool leftIsSpeaking;

	private InputActions input;

	private bool isLastLine;

	private float timeOpen;

	public bool IsOpen => UiParent.activeInHierarchy;

	public bool InputAllowed { get; private set; }

	private float timeLocked => OptionsManager.Instance.DialogueOptionTimeLock;

	private void Awake()
	{
		input = new InputActions();
		input.Main.Confirm.started += delegate
		{
			OnConfirm(isMouseClick: false);
		};
		input.Main.Cancel.started += delegate
		{
			OnGoBack(isMouseClick: false);
		};
		input.Main.Interact.started += delegate
		{
			OpenMonsterDetails();
		};
		input.Disable();
		InputReminder.Show(InputDescriptionProxy);
		InputReminder.gameObject.SetActive(value: false);
		UiParent.SetActive(value: false);
	}

	private void Start()
	{
		MouseInputController instance = MouseInputController.Instance;
		instance.OnLeftMousePressed = (MouseInputController.MouseClickDelegate)Delegate.Combine(instance.OnLeftMousePressed, new MouseInputController.MouseClickDelegate(OnMouseClicked));
		InputUtils.RegisterInput(input);
	}

	private void Update()
	{
		timeOpen += Time.unscaledDeltaTime;
	}

	public void Open(DialogueInteractable dialogue, UnityAction closeCallback)
	{
		currentDialogue = dialogue;
		input.Disable();
		InputAllowed = false;
		LeftCharacterDisplay.Hide(animate: false);
		RightCharacterDisplay.Hide(animate: false);
		UiParent.SetActive(value: true);
		if (closeCallback != null)
		{
			OnClose.AddListener(closeCallback);
		}
		timeOpen = 0f;
		WwiseAudioController.Instance.SetWwiseUiState(E_WwiseUiState.UiOpened);
		Color white = Color.white;
		white.a = 0f;
		ColorTweenData tweenData = new ColorTweenData
		{
			startColor = white,
			endColor = Color.white,
			duration = OpenDuration
		};
		BackgroundGradient.gameObject.SetActive(value: true);
		ColorTween.StartTween(BackgroundGradient.gameObject, tweenData);
		Timer.StartTimer(base.gameObject, OpenDuration, delegate
		{
			if (currentDialogue != null)
			{
				ShowDialogue(currentDialogue.GetNextDialogue());
			}
			input.Enable();
			InputAllowed = true;
		});
		GameStateManager.Instance.AddStateLayer(this, GameStateManager.EGameState.InMenu, GameStateManagerCallBackFunction);
	}

	public void Close(UnityAction callback = null)
	{
		input.Disable();
		StartCoroutine(HandleClosingSequence(callback));
		WwiseAudioController.Instance.SetWwiseUiState(E_WwiseUiState.UiClosed);
	}

	private void GameStateManagerCallBackFunction(bool isActiveLayer)
	{
		if (isActiveLayer && InputAllowed)
		{
			input.Enable();
		}
		else
		{
			input.Disable();
		}
	}

	private IEnumerator HandleClosingSequence(UnityAction callback = null)
	{
		currentDialogue.canBeInteracted = false;
		LeftCharacterDisplay.Hide(currentDialogueData.LeftCharacter != null);
		RightCharacterDisplay.Hide(currentDialogueData.RightCharacter != null);
		BackgroundGradient.gameObject.SetActive(value: false);
		yield return new WaitForSeconds(LeftCharacterDisplay.AnimationFadeOutEvent.length);
		Color white = Color.white;
		white.a = 0f;
		new ColorTweenData
		{
			startColor = Color.white,
			endColor = white,
			duration = CloseDuration
		};
		OnClose?.Invoke();
		OnClose.RemoveAllListeners();
		input.Disable();
		InputAllowed = false;
		Timer.StartTimer(base.gameObject, CloseDuration, delegate
		{
			UiParent.SetActive(value: false);
			GameStateManager.Instance.RemoveStateLayer(this);
			if (SceneManager.GetActiveScene() != SceneManager.GetSceneByPath(SceneLoader.Instance.DemoScene.ScenePath) && ExplorationController.Instance.CurrentArea != EArea.Void)
			{
				UIController.Instance.SetExplorationHUDVisibility(visible: true);
			}
			if (callback != null)
			{
				callback();
			}
		});
	}

	public void ShowDialogue(DialogueDisplayData displayData)
	{
		currentDialogueData = displayData;
		leftIsSpeaking = displayData.LeftIsSpeaking;
		SetupCharacterDisplays(displayData.LeftCharacter, displayData.RightCharacter);
		timeOpen = 0f;
		isLastLine = displayData.DialogueOptions.Length == 0;
		DialogueCharacterDisplay dialogueCharacterDisplay = (leftIsSpeaking ? LeftCharacterDisplay : RightCharacterDisplay);
		if (currentDialogueData.IsChoiceEvent)
		{
			dialogueCharacterDisplay.SetEventText(displayData.DialogueText, animate: true);
			dialogueCharacterDisplay.SetEventOptions(displayData.DialogueOptions, displayData.AllOptionsSmall, displayData.LastOptionIsSmall, displayData.ShowNewMarker);
			InputReminder.gameObject.SetActive(value: true);
			SetupInputReminder(currentDialogueData.EnableMonsterDetails, currentDialogue.IsGoBackPossible(out var _));
		}
		else
		{
			dialogueCharacterDisplay.SetDialogue(displayData.DialogueText, animate: true);
			dialogueCharacterDisplay.SetDialogOptions(displayData.DialogueOptions);
			InputReminder.gameObject.SetActive(value: false);
		}
	}

	public int GetSelectedDialogueOptionIndex()
	{
		return (leftIsSpeaking ? LeftCharacterDisplay : RightCharacterDisplay).GetSelectedOption();
	}

	private void SetupCharacterDisplays(DialogueCharacter leftCharacter, DialogueCharacter rightCharacter)
	{
		List<EDialogueRessource> displayedRessources = currentDialogue.GetCurrentNode().DisplayedRessources;
		if (leftCharacter != null)
		{
			LeftCharacterDisplay.SetCharacter(leftCharacter);
		}
		if (leftCharacter == null)
		{
			LeftCharacterDisplay.Hide(animate: false);
		}
		else if (leftIsSpeaking)
		{
			if (currentDialogueData.IsChoiceEvent)
			{
				LeftCharacterDisplay.ShowChoiceEvent(!LeftCharacterDisplay.IsTalking || LeftCharacterDisplay.IsDisplayingDialogue, leftIsSpeaking, displayedRessources);
			}
			else
			{
				LeftCharacterDisplay.ShowNormalDialogue(!LeftCharacterDisplay.IsTalking || LeftCharacterDisplay.IsDisplayingEvent, leftIsSpeaking, displayedRessources);
			}
		}
		else
		{
			LeftCharacterDisplay.ShowInactive(LeftCharacterDisplay.IsTalking || LeftCharacterDisplay.IsHidden, currentDialogueData.IsChoiceEvent);
		}
		if (rightCharacter != null)
		{
			RightCharacterDisplay.SetCharacter(rightCharacter);
		}
		if (rightCharacter == null)
		{
			RightCharacterDisplay.Hide(animate: false);
		}
		else if (!leftIsSpeaking)
		{
			if (currentDialogueData.IsChoiceEvent)
			{
				RightCharacterDisplay.ShowChoiceEvent(!RightCharacterDisplay.IsTalking || RightCharacterDisplay.IsDisplayingDialogue, !leftIsSpeaking, displayedRessources);
			}
			else
			{
				RightCharacterDisplay.ShowNormalDialogue(!RightCharacterDisplay.IsTalking || RightCharacterDisplay.IsDisplayingEvent, !leftIsSpeaking, displayedRessources);
			}
		}
		else
		{
			RightCharacterDisplay.ShowInactive(RightCharacterDisplay.IsTalking || RightCharacterDisplay.IsHidden, currentDialogueData.IsChoiceEvent);
		}
	}

	private void SetupInputReminder(bool enableMonsterDetails, bool isGoBackPossible)
	{
		List<ButtonViewSettings> list = new List<ButtonViewSettings>();
		if (enableMonsterDetails)
		{
			list.Add(ViewMonstersDescription);
		}
		if (isGoBackPossible)
		{
			list.Add(GoBackDescription);
		}
		InputDescriptionProxy.InputOptions = list.ToArray();
		InputReminder.Show(InputDescriptionProxy);
	}

	private void OnMouseClicked()
	{
		OnConfirm(isMouseClick: true);
	}

	public void OnConfirm(bool isMouseClick)
	{
		if (!input.Main.enabled || (timeOpen < timeLocked && currentDialogueData.IsChoiceEvent))
		{
			return;
		}
		DialogueCharacterDisplay dialogueCharacterDisplay = (leftIsSpeaking ? LeftCharacterDisplay : RightCharacterDisplay);
		if (isMouseClick && dialogueCharacterDisplay.HasSelectionOptions())
		{
			return;
		}
		if (!dialogueCharacterDisplay.IsTextComplete)
		{
			dialogueCharacterDisplay.ShowAllDialogueText();
		}
		else
		{
			if (currentDialogue == null)
			{
				return;
			}
			if (isLastLine)
			{
				isLastLine = false;
				UIController.Instance.SetDialogueVisibility(visible: false);
				currentDialogue.TriggerNodeOnCloseEvents();
				WwiseAudioController.Instance.PostWwiseEventGlobal("Play_SFX_menu_confirm");
				return;
			}
			currentDialogue.TriggerNodeOnCloseEvents();
			currentDialogue.SelectDialogueOption(dialogueCharacterDisplay.GetSelectedOption(), currentDialogueData.DialogueOptions.Length, out isLastLine, out var forceSkip);
			if (isLastLine && forceSkip)
			{
				isLastLine = false;
				UIController.Instance.SetDialogueVisibility(visible: false);
				WwiseAudioController.Instance.PostWwiseEventGlobal("Play_SFX_menu_confirm");
			}
			else
			{
				ShowDialogue(currentDialogue.GetNextDialogue());
				WwiseAudioController.Instance.PostWwiseEventGlobal("Play_SFX_menu_confirm");
			}
		}
	}

	public void OnGoBack(bool isMouseClick)
	{
		if (!input.Main.enabled || !currentDialogue.IsGoBackPossible(out var goBackToNode))
		{
			return;
		}
		DialogueCharacterDisplay dialogueCharacterDisplay = (leftIsSpeaking ? LeftCharacterDisplay : RightCharacterDisplay);
		if (!isMouseClick || !dialogueCharacterDisplay.HasSelectionOptions())
		{
			if (!dialogueCharacterDisplay.IsTextComplete)
			{
				dialogueCharacterDisplay.ShowAllDialogueText();
			}
			else if (!(currentDialogue == null))
			{
				currentDialogue.SelectGoBack(goBackToNode);
				ShowDialogue(currentDialogue.GetNextDialogue());
				WwiseAudioController.Instance.PostWwiseEventGlobal("Play_SFX_menu_confirm");
			}
		}
	}

	private void OpenMonsterDetails()
	{
		if (currentDialogueData != null && currentDialogueData.IsChoiceEvent && currentDialogueData.EnableMonsterDetails)
		{
			UIController.Instance.SetMonsterInfoVisibility(visible: true, CombatController.Instance.PlayerMonsters, ExplorationMonsterInfoMenu.EMonsterInfoState.LimitedMonsterInfo, 0, delegate
			{
				OnClosingMonsterInspector();
			}, delegate
			{
				OnClosingMonsterInspector();
			}, animateBackground: false, freezeTime: false);
			DialogueCharacterDisplay dialogueCharacterDisplay = (leftIsSpeaking ? LeftCharacterDisplay : RightCharacterDisplay);
			if (dialogueCharacterDisplay.DialogOptions.IsSelecting)
			{
				dialogueCharacterDisplay.DialogOptions.SetLocked(locked: true);
			}
			if (dialogueCharacterDisplay.ChoiceEventOptions.IsSelecting)
			{
				dialogueCharacterDisplay.ChoiceEventOptions.SetLocked(locked: true);
			}
			input.Disable();
			base.gameObject.SetActive(value: false);
		}
	}

	private void OnClosingMonsterInspector()
	{
		input.Enable();
		base.gameObject.SetActive(value: true);
		DialogueCharacterDisplay dialogueCharacterDisplay = (leftIsSpeaking ? LeftCharacterDisplay : RightCharacterDisplay);
		if (dialogueCharacterDisplay.DialogOptions.IsLocked)
		{
			dialogueCharacterDisplay.DialogOptions.SetLocked(locked: false);
		}
		if (dialogueCharacterDisplay.ChoiceEventOptions.IsLocked)
		{
			dialogueCharacterDisplay.ChoiceEventOptions.SetLocked(locked: false);
		}
	}
}