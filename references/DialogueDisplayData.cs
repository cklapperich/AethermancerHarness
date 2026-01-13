public class DialogueDisplayData
{
	private DialogueCharacter leftCharacter;

	private DialogueCharacter rightCharacter;

	private string dialogueText;

	private bool leftIsSpeaking;

	private string[] dialogueOptions;

	private bool isChoiceEvent;

	private bool allOptionsSmall;

	private bool lastOptionIsSmall;

	private bool enableMonsterDetails;

	private bool[] showNewMarker;

	public DialogueCharacter LeftCharacter => leftCharacter;

	public DialogueCharacter RightCharacter => rightCharacter;

	public string DialogueText => dialogueText;

	public bool LeftIsSpeaking => leftIsSpeaking;

	public string[] DialogueOptions => dialogueOptions;

	public bool IsChoiceEvent => isChoiceEvent;

	public bool AllOptionsSmall => allOptionsSmall;

	public bool LastOptionIsSmall => lastOptionIsSmall;

	public bool EnableMonsterDetails
	{
		get
		{
			if (enableMonsterDetails)
			{
				return isChoiceEvent;
			}
			return false;
		}
	}

	public bool[] ShowNewMarker => showNewMarker;

	public DialogueDisplayData(DialogueCharacter leftCharacter, DialogueCharacter rightCharacter, string dialogueText, bool leftIsSpeaking, string[] dialogueOptions = null, bool isChoiceEvent = false, bool allOptionsSmall = false, bool lastOptionIsSmall = false, bool enableMonsterDetails = false, bool[] showNewMarker = null)
	{
		this.leftCharacter = leftCharacter;
		this.rightCharacter = rightCharacter;
		this.dialogueText = dialogueText;
		this.leftIsSpeaking = leftIsSpeaking;
		this.dialogueOptions = dialogueOptions;
		this.isChoiceEvent = isChoiceEvent;
		this.allOptionsSmall = allOptionsSmall;
		this.lastOptionIsSmall = lastOptionIsSmall;
		this.enableMonsterDetails = enableMonsterDetails;
		this.showNewMarker = showNewMarker;
	}
}