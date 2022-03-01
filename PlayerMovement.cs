using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(PlayerCollision))]
[RequireComponent(typeof(Controls))]
public class PlayerMovement : MonoBehaviour
{
    public const float fps = 60;
    // hard-coded values
    // jumping consts
    public float JumpHeight { get; } = 3.5f;
    public int JumpFrames { get; } = 22;
    public int JumpReleaseFrames { get; } = 5;
    public float JumpBufferFrames { get; } = 4;
    public float CoyoteTimeFrames { get; } = 5;
    public int FallFrames { get; } = 10;
    public int FallGravityTransitionFrames { get; } = 25;
    public float MaxFallSpeed { get; } = -22f;
    // x movement consts
    public float MaxGroundSpeedX { get; } = 12.5f;
    public float MaxAirSpeedX { get; } = 11f;
    public int GroundAccelerationFrames { get; } = 13;
    public int GroundDeccelerationFrames { get; } = 2;
    public int AirAccelerationFrames { get; } = 8;
    public int AirDeccelerationFrames { get; } = 10;
    public int SwitchXDirectionFrames { get; } = 6;
    // wall interaction consts
    public float WallSlideSpeed { get; } = -6f;
    public int WallSlideTransitionFrames { get; } = 10;
    public int WallStickFrames { get; } = 8;
    public int WallJumpGraceFrames { get; } = 16;
    public int LongWallJumpCoyoteFrames { get; } = 4;
    public int WallJumpCoyoteFrames { get; } = 4;
    public int JumpBufferOverWallJumpBias { get; } = 2;
    public float ClimbSpeed { get; } = 6f;
    public int WallClimbAccelerationFrames { get; } = 10;
    public float MinGrabableEdgeFraction { get; } = 0.4f; // defined as a fraction of the player's height
    public readonly Vector2 horzWallJump = new Vector2(0.75f, 1f);
    public readonly Vector2 vertWallJump = new Vector2(0.7f, 0.8f);
    public readonly Vector2 boostOverEdgeSpeed = new Vector2(3f, 15f);
    // control default values
    public float VertSensitivity { get; } = 0.65f;
    public float HorzSensitivity { get; } = 0.75f;

    // property links
    public float Gravity => -2f * JumpHeight / Mathf.Pow(JumpFrames / fps, 2);
    public float JumpVelocity => 2f * JumpHeight / (JumpFrames / fps);
    public float FallGravity => -2f * JumpHeight / Mathf.Pow(FallFrames / fps, 2);
    public float OneFrameDeltaY => (Gravity * Time.fixedDeltaTime + 0.5f * Gravity * Time.fixedDeltaTime) * Time.fixedDeltaTime;
    public bool GoingMaxSpeedX => Controller.Collisions.below ? Mathf.Abs(velocity.x) == MaxGroundSpeedX : Mathf.Abs(velocity.x) == MaxAirSpeedX;
    public bool GoingMaxSpeedY => velocity.y == MaxFallSpeed;
    // rate of change values
    public float GroundAcceleration => MaxGroundSpeedX / GroundAccelerationFrames;
    public float GroundDecceleration => MaxGroundSpeedX / GroundDeccelerationFrames;
    public float AirAcceleration => MaxAirSpeedX / AirAccelerationFrames;
    public float AirDecceleration => MaxAirSpeedX / AirDeccelerationFrames;
    public float WallSlideTransitionRate => Mathf.Abs(MaxFallSpeed - WallSlideSpeed) / WallSlideTransitionFrames;
    public float WallClimbAcceleration => ClimbSpeed / WallClimbAccelerationFrames;
    public float JumpReleaseDecceleration => JumpVelocity / JumpReleaseFrames;
    public float FallGravityTransitionRate => Mathf.Abs(Gravity - FallGravity) / FallGravityTransitionFrames;
    public float MinGrabableEdgeSize => Controller.GetBounds().size.y * Mathf.Clamp(MinGrabableEdgeFraction, 0f, 0.5f);
    //other
    public int PlayerDimension => GetObjectDimension(gameObject);

    public Vector3 velocity;
    // properties
    // movement vars
    public float MoveInput { get; private set; }
    public float ClimbInput { get; private set; }
    public float CurrentGravity { get; private set; }
    public int LookingDir { get; private set; }
    // public set bools
    public bool CanWallSlide { get; set; } = true;
    public bool CanWallJump { get; set; } = true;
    public bool CanWallClimb { get; set; } = true;
    // private set bools
    public bool WallSliding { get; private set; }
    public bool WallSticking { get; private set; }
    public bool HoldingGrab { get; private set; }
    public bool HoldingJump { get; private set; }
    public bool BoostingOverEdge { get; private set; }

    // non-autoproperties
    private float targetVelocityX;
    public float TargetVelocityX {
        get => targetVelocityX;
        private set {
            int prev = Math.Sign(targetVelocityX);
            if (Math.Sign(value) != prev && prev != 0f) {
                timer.SwitchXDirection = SwitchXDirectionFrames / fps;
            }
            targetVelocityX = value;
        }
    }

    private bool wallClimbing;
    public bool WallClimbing {
        get => wallClimbing;
        private set {
            // when the player starts wall climbing, cancel vertical momentum
            if (value != wallClimbing && value == true) {
                velocity.y = 0f;
            }
            wallClimbing = value;
        }
    }

    private int wallDirX;
    private int prevWallDirX;
    public int WallDirX {
        get => wallDirX;
        private set {
            if (value != wallDirX) {
                prevWallDirX = wallDirX;
                wallDirX = value;
            }
        }
    }

    // class objects
    public PlayerCollision Controller { get; private set; }
    private HandleTiles seperateTiles;
    private Controls controls;
    private Timer timer;
    private EventManager eventManager;


    private void Awake() {
        timer = new Timer();
        controls = new Controls();
        eventManager = FindObjectOfType<EventManager>();

        Controller = GetComponent<PlayerCollision>();
        seperateTiles = FindObjectOfType<HandleTiles>();
        Time.fixedDeltaTime = 1 / fps;

        if (PlayerDimension == 0) {
            controls.Player0.Move.performed += ctx => MoveInput = (ctx.ReadValue<Vector2>().y >= -HorzSensitivity) ? Math.Sign(ctx.ReadValue<Vector2>().x) : 0f;
            controls.Player0.Move.canceled += ctx => MoveInput = 0f;
            controls.Player0.Jump.started += ctx => StartJump();
            controls.Player0.Jump.canceled += ctx => HoldingJump = false;
        } else if (PlayerDimension == 1) {
            controls.Player1.Move.performed += ctx => MoveInput = (ctx.ReadValue<Vector2>().y >= -HorzSensitivity) ? Math.Sign(ctx.ReadValue<Vector2>().x) : 0f;
            controls.Player1.Move.canceled += ctx => MoveInput = 0f;
            controls.Player1.Jump.started += ctx => StartJump();
            controls.Player1.Jump.canceled += ctx => HoldingJump = false;
        }
    }

    private void OnEnable() {
        controls.Player0.Enable();
        controls.Player1.Enable();
        controls.UI.Enable();
    }

    private void OnDisable() {
        controls.Player0.Disable();
        controls.Player1.Disable();
        controls.UI.Disable();
    }


    private void Start() {
        MoveToSpawn();
    }

    private void MoveToSpawn() {
        Bounds bounds = Controller.GetBounds(true);
        float xPos = seperateTiles.PlayerSpawns[PlayerDimension].x + bounds.size.x / 2f;
        float yPos = seperateTiles.PlayerSpawns[PlayerDimension].y + bounds.size.y / 2f;
        transform.position = new Vector3(xPos, yPos, 0f);
    }


    private void FixedUpdate() {
        WallDirX = Controller.TestCollision(Vector2.left, PlayerDimension) ? -1 : Controller.TestCollision(Vector2.right, PlayerDimension) ? 1 : 0;
        if (Controller.TestCollision(Vector2.left, PlayerDimension) && Controller.TestCollision(Vector2.right, PlayerDimension)) {
            WallDirX = (MoveInput == 0f) ? LookingDir : (int)MoveInput;
        }

        CanWallJump = CanWallSlide = CanWallClimb = true;
        if (WallDirX != 0) {
            List<string> tiles = Controller.GetTilesInDirection(Vector2.right * WallDirX, rayLength: 1f);
            foreach (var tile in tiles) {
                if (tile == "invis_border") {
                    CanWallJump = CanWallSlide = CanWallClimb = false;
                    WallDirX = 0;
                }
            }
        }
    
        CheckWallClimb();
        CheckJump();
        CheckWallJump();
        CheckWallSlide();
        
        CurrentGravity = (velocity.y >= 0f || Controller.Collisions.below) ? Gravity : Mathf.Clamp(CurrentGravity - FallGravityTransitionRate, FallGravity, float.MaxValue);
        if (WallClimbing) {
            CurrentGravity = 0f;
        }

        UpdateXVelocity();
        UpdateYVelocity(CurrentGravity);

        // update which way player is looking after velocity is calculated
        if (Math.Sign(velocity.x) != 0) {
            LookingDir = Math.Sign(velocity.x);
        }

        // using velocity verlet
        Vector3 deltaPos = (velocity + Vector3.up * 0.5f * CurrentGravity * Time.fixedDeltaTime) * Time.fixedDeltaTime;
        Controller.Move(deltaPos);

        velocity.y *= (Controller.Collisions.below || Controller.Collisions.above ? 0f : 1f);
        velocity.x *= (Controller.Collisions.left || Controller.Collisions.right ? 0f : 1f);

        timer.Update();
    }


    private void UpdateXVelocity() {
        TargetVelocityX = (Controller.Collisions.below ? MaxGroundSpeedX : MaxAirSpeedX) * MoveInput;
        if (BoostingOverEdge && MoveInput != -LookingDir) {
            TargetVelocityX = LookingDir * boostOverEdgeSpeed.x;
        }

        // instantly switching directions if the player doubles back
        if (Controller.Collisions.below && MoveInput == -LookingDir && timer.SwitchXDirection > 0) {
                velocity.x = 0f;
                timer.SwitchXDirection = 0f;
        }
        timer.SwitchXDirection *= !Controller.Collisions.below ? 0f : 1f;

        // cancel wall jump grace period if moving away from the wall
        timer.WallJumpGrace *= (MoveInput == Math.Sign(velocity.x) ? 0f : 1f);

        bool movingIntoWall = WallDirX == MoveInput && MoveInput != 0f;  
        // no x velocity if wall sticking, in wall jump grace period, or wall climbing
        TargetVelocityX *= ((WallSticking && CanWallJump || timer.WallJumpGrace > 0f && CanWallJump || (WallClimbing && !movingIntoWall)) ? 0f : 1f);

        // acceleration and decceleration
        int dir = TargetVelocityX > velocity.x ? 1 : -1;
        if (Mathf.Abs(velocity.x) < Mathf.Abs(TargetVelocityX) || Math.Sign(velocity.x) == -Math.Sign(TargetVelocityX)) {
            velocity.x += (Controller.Collisions.below ? GroundAcceleration : AirAcceleration) * dir;
        } else {
            velocity.x += (Controller.Collisions.below ? GroundDecceleration : AirDecceleration) * dir;
        }
        velocity.x = (dir == 1) ? Mathf.Clamp(velocity.x, float.MinValue, TargetVelocityX) : Mathf.Clamp(velocity.x, TargetVelocityX, float.MaxValue);
    }


    private void UpdateYVelocity(float currentGravity) {
        if (BoostingOverEdge && Controller.TestCollision(new Vector2(LookingDir, 0), PlayerDimension)) {
            velocity.y = boostOverEdgeSpeed.y;
        } else if (WallClimbing) {
            velocity.y += WallClimbAcceleration * ClimbInput;
            velocity.y = (ClimbInput != 0f) ? Mathf.Clamp(velocity.y, -ClimbSpeed, ClimbSpeed) : 0f;
        } else if (WallSliding) {
            int dir = (WallSlideSpeed > velocity.y) ? 1 : -1;
            velocity.y += WallSlideTransitionRate * dir;
            velocity.y = (dir == 1) ? Mathf.Clamp(velocity.y, float.MinValue, WallSlideSpeed) : Mathf.Clamp(velocity.y, WallSlideSpeed, float.MaxValue);
        } else if ((!HoldingJump || BoostingOverEdge) && velocity.y > 0f) {
            // short jump
            velocity.y = Mathf.Clamp(velocity.y - JumpReleaseDecceleration, 0f, float.MaxValue);
        } else {
            velocity.y += currentGravity * Time.fixedDeltaTime;
        }

        velocity.y = Mathf.Clamp(velocity.y, MaxFallSpeed, float.MaxValue);
    }


    private void CheckWallClimb() {
        bool[] hits = Controller.TestCollision(new Vector2(WallDirX, 0), PlayerDimension, inset: MinGrabableEdgeSize);
        bool letGoAtBottom = hits[0] && !(hits[1] || hits[2] || hits[3]);
        bool letGoAtTop = hits[3] && !(hits[0] || hits[1] || hits[2]);

        BoostingOverEdge &= !(wallClimbing || Controller.Collisions.below);
        BoostingOverEdge |= letGoAtTop && ClimbInput == 1 || BoostingOverEdge;

        WallClimbing = HoldingGrab && WallDirX != 0 && CanWallClimb && !letGoAtBottom && !letGoAtTop;

        if (WallClimbing) {
            LookingDir = WallDirX;
        }
    }


    private void StartJump() {
        timer.JumpBuffer = JumpBufferFrames / fps;
        if (!Controller.Collisions.below) {
            timer.WallJumpBuffer = JumpBufferFrames / fps;
        }
        HoldingJump = true;
    }

    private void CheckJump() {
        if (Controller.Collisions.below) {
            timer.CoyoteTime = CoyoteTimeFrames / fps;
        }

        // jumpBuffer time is set when the jump is input, and CheckJump executes every frame
        if (Controller.Collisions.below && timer.JumpBuffer > 0f || timer.CoyoteTime > 0f && timer.JumpBuffer > 0f) {
            velocity.y = JumpVelocity;
            timer.CoyoteTime = 0f;
            timer.JumpBuffer = 0f;

            eventManager.playerJumpStarted?.Invoke(PlayerDimension);
        }
    }


    private void CheckWallJump() {
        // coyote time for max height wall jump
        if (timer.LongWallJumpCoyoteTime > 0f && MoveInput == Math.Sign(velocity.x) && !Controller.Collisions.below && MoveInput != 0f && CanWallJump) {
            WallJump(horzWallJump, (int)MoveInput);
            timer.LongWallJumpCoyoteTime = 0f;
        }

        // don't wall jump if buffered jump input would execute once hitting the ground
        float distanceToGround = Controller.DistanceTo(Vector2.down, PlayerDimension);
        bool jumpInstead = Mathf.Abs(PredictDeltaY((int)JumpBufferFrames + JumpBufferOverWallJumpBias)) > distanceToGround && MoveInput == 0f;

        bool againstJumpableWall = WallDirX != 0 && (!jumpInstead || WallClimbing || WallSliding);
        if (againstJumpableWall) {
            timer.WallJumpCoyoteTime = WallJumpCoyoteFrames / fps;
        }
        bool coyoteWallJumping = !againstJumpableWall && timer.WallJumpCoyoteTime > 0f;
        
        if (timer.WallJumpBuffer > 0f && CanWallJump && (againstJumpableWall || coyoteWallJumping)) {
            int wallJumpingDir = coyoteWallJumping ? -prevWallDirX : -WallDirX;
            WallJump((MoveInput == wallJumpingDir) ? horzWallJump : vertWallJump, wallJumpingDir);
        }
    }

    public void WallJump(Vector2 wallJumpComponents, int xDir) {
        velocity = JumpVelocity * wallJumpComponents;
        velocity.x *= xDir;

        eventManager.playerJumpStarted?.Invoke(PlayerDimension);
        timer.WallJumpGrace = WallJumpGraceFrames / fps;
        timer.LongWallJumpCoyoteTime = LongWallJumpCoyoteFrames / fps;
        timer.WallJumpCoyoteTime = 0f;
        WallClimbing = false;
    }


    private void CheckWallSlide() {
        // if not holding away from wall keep sticking
        if (MoveInput != -WallDirX) {
            timer.WallStick = WallStickFrames / fps;
        }

        WallSliding = MoveInput == WallDirX && WallDirX != 0 && velocity.y < 0 && CanWallSlide && !Controller.Collisions.above;
        WallSticking = WallDirX != 0 && timer.WallStick > 0f && !Controller.Collisions.below && !HoldingJump && !wallClimbing;

        if (WallSliding && WallDirX == -1 && !WallClimbing) {
            eventManager.playerWallSlideLeft?.Invoke(PlayerDimension);
        } else {
            eventManager.playerNotWallSlideLeft?.Invoke(PlayerDimension);
        }
        if (WallSliding && WallDirX == 1 && !WallClimbing) {
            eventManager.playerWallSlideRight?.Invoke(PlayerDimension);
        } else {
            eventManager.playerNotWallSlideRight?.Invoke(PlayerDimension);
        }
    }


    // simulates the player's change in height if no inputs were made over a specified number of frames
    private float PredictDeltaY(int frames) {
        float deltaY = 0f;
        float currentGravity = CurrentGravity;
        float yVelocity = velocity.y;
        
        for (int i = 0; i < frames; i++) {
            currentGravity = (yVelocity >= 0f) ? Gravity : Mathf.Clamp(currentGravity - FallGravityTransitionRate, FallGravity, float.MaxValue);
            yVelocity += currentGravity * Time.fixedDeltaTime;
            yVelocity = Mathf.Clamp(yVelocity, MaxFallSpeed, float.MaxValue);
            deltaY += (yVelocity + 0.5f * currentGravity * Time.fixedDeltaTime) * Time.fixedDeltaTime;
        }

        return deltaY;
    }

    public int GetObjectDimension(GameObject thisObject) {
        string[] sections = thisObject.name.Split('_');
        return int.Parse(sections[sections.Length - 1]);
    }
    public int GetObjectLayer(string name) {
        string[] sections = name.Split('_');
        return int.Parse(sections[sections.Length - 1]);
    }


    private class Timer {
        public float SwitchXDirection { get; set; }
        public float JumpBuffer { get; set; }
        public float CoyoteTime { get; set; }
        public float WallStick { get; set; }
        public float WallJumpGrace { get; set; }
        public float WallJumpBuffer { get; set; }
        public float LongWallJumpCoyoteTime { get; set; }
        public float WallJumpCoyoteTime { get; set; }

        public void Update() {
            SwitchXDirection -= Time.fixedDeltaTime;
            JumpBuffer -= Time.fixedDeltaTime;
            CoyoteTime -= Time.fixedDeltaTime;
            WallStick -= Time.fixedDeltaTime;
            WallJumpGrace -= Time.fixedDeltaTime;
            WallJumpBuffer -= Time.fixedDeltaTime;
            LongWallJumpCoyoteTime -= Time.fixedDeltaTime;
            WallJumpCoyoteTime -= Time.fixedDeltaTime;
        }
    }
}