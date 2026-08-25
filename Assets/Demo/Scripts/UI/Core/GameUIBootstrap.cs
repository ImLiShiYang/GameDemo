using UnityEngine;

/// <summary>
/// 游戏场景 UI 启动入口。
/// 游戏开始后自动打开 HUD，并在第一次游玩时打开 Tutorial。
/// </summary>
public class GameUIBootstrap : MonoBehaviour
{
    [Header("Player")]
    [SerializeField]
    private Health playerHealth;

    [SerializeField]
    private PlayerExperience playerExperience;

    [Header("Startup")]
    [SerializeField]
    private bool openHudOnStart = true;

    [SerializeField]
    private bool openTutorialOnStart = true;
    
    [SerializeField]
    private bool openMainMenuOnStart = true;
    
    private UIManager cachedUIManager;
    private static bool startGameDirectlyOnNextLoad;

    private void Awake()
    {
        TryFindPlayerReferences();
    }

    private void OnEnable()
    {
        MainMenuPanel.GameStarted +=
            HandleGameStarted;
    }

    private void OnDisable()
    {
        MainMenuPanel.GameStarted -=
            HandleGameStarted;
    }
    
    public static void StartGameDirectlyOnNextLoad()
    {
        startGameDirectlyOnNextLoad = true;
    }
    
    private void Start()
    {
        if (GameEntry.Instance == null)
        {
            Debug.LogError(
                "GameUIBootstrap 找不到 GameEntry。",
                this
            );

            return;
        }

        UIManager uiManager = GameEntry.UI;

        if (uiManager == null)
        {
            return;
        }

        if (startGameDirectlyOnNextLoad)
        {
            startGameDirectlyOnNextLoad = false;

            Time.timeScale = 1f;

            StartGameplay();
        }
        else if (openMainMenuOnStart && uiManager.HasConfig(UIType.MainMenu))
        {
            uiManager.Open(UIType.MainMenu);
        }
        else if (openHudOnStart && uiManager.HasConfig(UIType.HUD))
        {
            StartGameplay();
        }
        
    }

    private void HandleGameStarted()
    {
        StartGameplay();
    }
    
    private void StartGameplay()
    {
        UIManager uiManager = GameEntry.UI;

        if (uiManager == null)
        {
            return;
        }

        OpenHUD(uiManager);

        bool shouldOpenTutorial =
            openTutorialOnStart &&
            !TutorialPanel.HasBeenShown &&
            uiManager.HasConfig(UIType.Tutorial);

        if (shouldOpenTutorial)
        {
            uiManager.Open(
                UIType.Tutorial,
                new TutorialOpenArgs
                {
                    MarkAsShown = true,
                    OnConfirmed = StartWaves
                }
            );

            return;
        }

        StartWaves();
    }
    
    private void StartWaves()
    {
        WaveManager waveManager = GameEntry.Wave;

        if (waveManager != null)
        {
            waveManager.StartWaves();
        }
    }
    
    private void OpenHUD(UIManager uiManager)
    {
        HUDOpenArgs args =
            new HUDOpenArgs
            {
                PlayerHealth = playerHealth,
                PlayerExperience = playerExperience
            };

        uiManager.Open<HUDPanel>(
            UIType.HUD,
            args
        );
    }
    
    private void TryFindPlayerReferences()
    {
        if (playerHealth != null &&
            playerExperience != null)
        {
            return;
        }

        GrayboxPlayerController player =
            FindObjectOfType<GrayboxPlayerController>();

        if (player == null)
        {
            return;
        }

        if (playerHealth == null)
        {
            playerHealth =
                player.GetComponent<Health>();
        }

        if (playerExperience == null)
        {
            playerExperience =
                player.GetComponent<PlayerExperience>();
        }
    }
}
