using UnityEngine;
using UnityEngine.UI;

public class TutorialPanel : UIBase
{
    public const string TutorialPlayerPrefsKey =
        "GameDemo_TutorialShown";

    public static bool HasBeenShown =>
        PlayerPrefs.GetInt(
            TutorialPlayerPrefsKey,
            0
        ) == 1;

    [SerializeField]
    private Button confirmButton;

    [SerializeField]
    private bool pauseGame = true;

    private float previousTimeScale = 1f;

    protected override void OnInit()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(
                Confirm
            );
        }
    }

    protected override void OnOpen(object args)
    {
        if (!pauseGame)
        {
            return;
        }

        previousTimeScale =
            Time.timeScale;

        Time.timeScale = 0f;
    }

    protected override void OnClose()
    {
        if (!pauseGame)
        {
            return;
        }

        Time.timeScale =
            previousTimeScale;
    }

    private void Confirm()
    {
        PlayerPrefs.SetInt(
            TutorialPlayerPrefsKey,
            1
        );

        PlayerPrefs.Save();

        CloseSelf();
    }

    [ContextMenu("Reset Tutorial Flag")]
    private void ResetTutorialFlag()
    {
        PlayerPrefs.DeleteKey(
            TutorialPlayerPrefsKey
        );

        PlayerPrefs.Save();

        Debug.Log(
            "新手提示记录已经重置。",
            this
        );
    }
}
