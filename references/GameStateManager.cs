using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameStateManager : MonoBehaviour
{
	public enum EGameState
	{
		InMenu,
		InMainMenu,
		InCinematic,
		InTransition,
		InTargetingVoidBlitz,
		InExploration,
		InCombat,
		InSceneTransition
	}

	public delegate void StateUpdateMethod(bool isActive);

	public class StateLayer
	{
		public object Source;

		public EGameState EGameState;

		public bool FreezesTime;

		public StateUpdateMethod StateUpdateMethod;

		public bool CurrentStateBool;

		public bool LastStateBool = true;
	}

	protected List<StateLayer> PriorityStateLayers = new List<StateLayer>();

	protected List<StateLayer> StateLayers = new List<StateLayer>();

	public static GameStateManager Instance => GameController.Instance.GameState;

	[field: SerializeField]
	public EGameState CurrentState { get; private set; }

	public bool IsPriorityLayerOpen => PriorityStateLayers.Count > 0;

	public StateLayer CurrentStateLayer
	{
		get
		{
			if (StateLayers.Count <= 0)
			{
				return new StateLayer
				{
					EGameState = BaseState
				};
			}
			List<StateLayer> stateLayers = StateLayers;
			return stateLayers[stateLayers.Count - 1];
		}
	}

	public EGameState BaseState { get; private set; }

	public int FinishedEncounterCount { get; set; }

	public int BiomeTier
	{
		get
		{
			if (!(LevelGenerator.Instance != null))
			{
				return 1;
			}
			return LevelGenerator.Instance.LevelBiome.Tier;
		}
	}

	protected EGameState PreviousBaseState { get; private set; }

	public bool IsLoadingScene { get; private set; }

	public bool IsPaused => TimeScaleManager.Instance.IsPaused;

	public bool IsCinematic => CurrentState == EGameState.InCinematic;

	public bool IsMenu => CurrentState == EGameState.InMenu;

	public bool IsMainMenu => CurrentState == EGameState.InMainMenu;

	public bool IsCombat
	{
		get
		{
			if (CurrentState != EGameState.InCombat)
			{
				return BaseState == EGameState.InCombat;
			}
			return true;
		}
	}

	public bool IsExploringOrInterrupted
	{
		get
		{
			if (CurrentState != EGameState.InExploration)
			{
				return BaseState == EGameState.InExploration;
			}
			return true;
		}
	}

	public bool IsExploring => CurrentState == EGameState.InExploration;

	public bool IsTargetingForVoidBlitz => CurrentState == EGameState.InTargetingVoidBlitz;

	private void Awake()
	{
		SetBaseState(EGameState.InMainMenu);
	}

	public void ClearCinematicSources()
	{
		StateLayers.Clear();
	}

	public void SetBaseState(EGameState state)
	{
		if (BaseState != state)
		{
			PreviousBaseState = BaseState;
			BaseState = state;
			UpdateState();
		}
	}

	public void SetIsLoadingScene(bool isLoading)
	{
		IsLoadingScene = isLoading;
		UpdateState();
	}

	public void AddStateLayer(object source, EGameState gameState = EGameState.InCinematic, StateUpdateMethod stateUpdateMethod = null, bool isPriorityLayer = false, bool freezesTime = false)
	{
		if (!StateLayers.Any((StateLayer cinematicSource) => cinematicSource.Source == source) && !PriorityStateLayers.Any((StateLayer cinematicSource) => cinematicSource.Source == source))
		{
			StateLayer item = new StateLayer
			{
				Source = source,
				EGameState = gameState,
				FreezesTime = freezesTime,
				StateUpdateMethod = stateUpdateMethod
			};
			if (!isPriorityLayer)
			{
				StateLayers.Add(item);
			}
			else
			{
				PriorityStateLayers.Add(item);
			}
			if (freezesTime)
			{
				TimeScaleManager.Instance.SetPause(isPaused: true);
			}
			UpdateState(source);
		}
	}

	public void RemoveStateLayer(object source)
	{
		if (!StateLayers.All((StateLayer cinematicSource) => cinematicSource.Source != source) || !PriorityStateLayers.All((StateLayer cinematicSource) => cinematicSource.Source != source))
		{
			StateLayers.RemoveAll((StateLayer cinematicSource) => cinematicSource.Source == source);
			PriorityStateLayers.RemoveAll((StateLayer cinematicSource) => cinematicSource.Source == source);
			if (StateLayers.All((StateLayer cinematicSource) => !cinematicSource.FreezesTime) && PriorityStateLayers.All((StateLayer cinematicSource) => !cinematicSource.FreezesTime))
			{
				TimeScaleManager.Instance.SetPause(isPaused: false);
			}
			UpdateState();
		}
	}

	public bool HasStateLayer(object source)
	{
		return StateLayers.Any((StateLayer stateLayer) => stateLayer.Source == source);
	}

	public bool StateLayerOpenBesides(object source)
	{
		if (!StateLayers.Any((StateLayer stateLayer) => stateLayer.Source != source))
		{
			return PriorityStateLayers.Any((StateLayer stateLayer) => stateLayer.Source != source);
		}
		return true;
	}

	public StateLayer? GetStateLayer(object source)
	{
		foreach (StateLayer priorityStateLayer in PriorityStateLayers)
		{
			if (priorityStateLayer.Source == source)
			{
				return priorityStateLayer;
			}
		}
		foreach (StateLayer stateLayer in StateLayers)
		{
			if (stateLayer.Source == source)
			{
				return stateLayer;
			}
		}
		return null;
	}

	public void Clear()
	{
		FinishedEncounterCount = 0;
		StateLayers.Clear();
		UpdateState();
	}

	private void SetState(EGameState state)
	{
		if (CurrentState != state)
		{
			CurrentState = state;
		}
	}

	private void UpdateState(object forceStateUpDateMethodeForObject = null)
	{
		SetCurrentState();
		CallStateUpdateMethods(forceStateUpDateMethodeForObject);
		SetLastState();
	}

	private void CallStateUpdateMethods(object forceStateUpDateMethodeForObject)
	{
		foreach (StateLayer priorityStateLayer in PriorityStateLayers)
		{
			if (priorityStateLayer.StateUpdateMethod != null && (priorityStateLayer.CurrentStateBool != priorityStateLayer.LastStateBool || priorityStateLayer.Source == forceStateUpDateMethodeForObject))
			{
				priorityStateLayer.StateUpdateMethod(priorityStateLayer.CurrentStateBool);
			}
		}
		foreach (StateLayer stateLayer in StateLayers)
		{
			if (stateLayer.StateUpdateMethod != null && (stateLayer.CurrentStateBool != stateLayer.LastStateBool || stateLayer.Source == forceStateUpDateMethodeForObject))
			{
				stateLayer.StateUpdateMethod(stateLayer.CurrentStateBool);
			}
		}
	}

	private void SetCurrentState()
	{
		RemoveNullSources(StateLayers);
		RemoveNullSources(PriorityStateLayers);
		if (IsLoadingScene)
		{
			SetState(EGameState.InSceneTransition);
		}
		else if (PriorityStateLayers.Count >= 1)
		{
			List<StateLayer> priorityStateLayers = PriorityStateLayers;
			SetState(priorityStateLayers[priorityStateLayers.Count - 1].EGameState);
		}
		else if (StateLayers.Count >= 1)
		{
			List<StateLayer> stateLayers = StateLayers;
			SetState(stateLayers[stateLayers.Count - 1].EGameState);
		}
		else
		{
			SetState(BaseState);
		}
		foreach (StateLayer priorityStateLayer in PriorityStateLayers)
		{
			object source = priorityStateLayer.Source;
			List<StateLayer> priorityStateLayers2 = PriorityStateLayers;
			priorityStateLayer.CurrentStateBool = source == priorityStateLayers2[priorityStateLayers2.Count - 1].Source;
		}
		foreach (StateLayer stateLayer in StateLayers)
		{
			int currentStateBool;
			if (PriorityStateLayers.Count < 1)
			{
				object source2 = stateLayer.Source;
				List<StateLayer> stateLayers2 = StateLayers;
				currentStateBool = ((source2 == stateLayers2[stateLayers2.Count - 1].Source) ? 1 : 0);
			}
			else
			{
				currentStateBool = 0;
			}
			stateLayer.CurrentStateBool = (byte)currentStateBool != 0;
		}
	}

	private void SetLastState()
	{
		foreach (StateLayer priorityStateLayer in PriorityStateLayers)
		{
			object source = priorityStateLayer.Source;
			List<StateLayer> priorityStateLayers = PriorityStateLayers;
			priorityStateLayer.LastStateBool = source == priorityStateLayers[priorityStateLayers.Count - 1].Source;
		}
		foreach (StateLayer stateLayer in StateLayers)
		{
			int lastStateBool;
			if (PriorityStateLayers.Count < 1)
			{
				object source2 = stateLayer.Source;
				List<StateLayer> stateLayers = StateLayers;
				lastStateBool = ((source2 == stateLayers[stateLayers.Count - 1].Source) ? 1 : 0);
			}
			else
			{
				lastStateBool = 0;
			}
			stateLayer.LastStateBool = (byte)lastStateBool != 0;
		}
	}

	private void RemoveNullSources(List<StateLayer> stateLayers)
	{
		for (int num = stateLayers.Count - 1; num >= 0; num--)
		{
			StateLayer stateLayer = stateLayers[num];
			if (stateLayer.Source is Object obj && !(obj != null))
			{
				Debug.LogError($"There is a StateLayer where the Source is a MissingReferenceException! The state will be removed but please tell a programmer to investigate this! State: {stateLayer.EGameState}");
				stateLayers.RemoveAt(num);
			}
		}
	}
}