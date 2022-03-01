using System;
using UnityEngine;

public class PlayerAnimation : MonoBehaviour
{
    // hard-coded values
    public readonly Vector3 stretchScale = new Vector3(0.9f, 1.2f, 1);
    public readonly Vector3 largestSquashScale = new Vector3(1.1f, 0.8f, 1);
    public readonly Vector3 smallestSquashScale = new Vector3(1.05f, 0.95f, 1);
    public float SquashAnimationTime { get; } = 0.15f;
    public float StretchAnimationTime { get; } = 0.125f;
    public bool CurrentlyScaling => currentScalingAnimation != null;
    public Color[] PlayerColor { get; } = { new Color(0.98f, 0.52f, 0.52f), new Color(0.33f, 0.67f, 0.97f) };

    // properties
    public float SquashStretchTimer { get; private set; }
    public Vector3 CurrentSquashScale { get; private set; }
 
    private int PlayerDimension => playerMovement.PlayerDimension;

    private delegate void CurrentScalingAnimation();
    private CurrentScalingAnimation currentScalingAnimation;

    private BoxCollider2D boxCollider;
    private PlayerMovement playerMovement;
    private SpriteRenderer spriteRenderer;


    private void Awake() {
        playerMovement = transform.parent.GetComponent<PlayerMovement>();
        boxCollider = transform.parent.GetComponent<BoxCollider2D>();
        if (boxCollider is null) {
            throw new ArgumentNullException("BoxCollider component in PlayerAnimation is null");
        }

        FindObjectOfType<EventManager>().playerJumpStarted += OnPlayerJumping;
        FindObjectOfType<EventManager>().playerLanded += OnPlayerLanding;
    }

    private void Start() {
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.color = PlayerColor[PlayerDimension];
    }


    public void Update() {
        currentScalingAnimation?.Invoke();
        SquashStretchTimer += Time.fixedDeltaTime;
    }


    private void SquashAnimation() {
        Vector3 currentScale = QuadraticInterpolation(Vector3.one, CurrentSquashScale, SquashStretchTimer, SquashAnimationTime);

        if (SquashStretchTimer >= SquashAnimationTime) {
            currentScalingAnimation = null;
            ScaleSprite(Vector3.one);
        } else {
            ScaleSprite(currentScale);
        }
    }

    private void StretchAnimation() {
        Vector3 currentScale = QuadraticInterpolation(Vector3.one, stretchScale, SquashStretchTimer, StretchAnimationTime);
        
        if (SquashStretchTimer >= StretchAnimationTime) {
            currentScalingAnimation = null;
            ScaleSprite(Vector3.one);
        } else {
            ScaleSprite(currentScale);
        }
    }


    private Vector3 QuadraticInterpolation(Vector3 endpoints, Vector3 peak, float currentTime, float cycleTime) {
        float progress = -4 * Mathf.Pow(currentTime / cycleTime, 2) + 4 * (currentTime / cycleTime);
        progress = Mathf.Clamp(progress, 0f, 1f);

        return Vector3.Lerp(endpoints, peak, progress);
    }

    private void ScaleSprite(Vector3 scale) {
        transform.localScale = scale;
        // keep the box collider at the bottom of the scaled sprite
        transform.localPosition = new Vector3(0, Math.Abs(boxCollider.size.y * (1 - scale.y) / 2f)) * Math.Sign(scale.y - 1);
    }


    private void OnPlayerLanding(int playerNum) {
        if (playerNum == PlayerDimension) {
            SquashStretchTimer = Time.fixedDeltaTime;
            float playerPercentMaxFallSpeed = Mathf.Abs(playerMovement.velocity.y / playerMovement.MaxFallSpeed);
            CurrentSquashScale = Vector3.Lerp(smallestSquashScale, largestSquashScale, playerPercentMaxFallSpeed);
            currentScalingAnimation = SquashAnimation;
        }
    }

    private void OnPlayerJumping(int playerNum) {
        if (playerNum == PlayerDimension) {
            SquashStretchTimer = Time.fixedDeltaTime;
            currentScalingAnimation = StretchAnimation;
        }
    }
}
