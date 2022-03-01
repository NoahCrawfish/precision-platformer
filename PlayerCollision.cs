using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

//[RequireComponent(typeof(BoxCollider2D))]
public class PlayerCollision : MonoBehaviour
{
    // hard-coded values
    private const int blocksLayer = 10;
    private const float skinWidth = 0.015f;
    private const int defaultRayCount = 4;

    public int[] BlockDimensionLayers { get; } = { 13, 14 };
    public int HorzRays { get; } = 4;
    public int VertRays { get; } = 4;
    public float TopInset { get; } = 0.2f;
    public float BottomInset { get; } = 0.175f;
    public float HorzInset { get; } = 0.25f;
    public float MinNudgeDeltaY { get; } = 0.35f;
    public float WallClimbSnapDistance { get; } = 0.25f;

    // properties
    public LayerMask CollisionMask { get; private set; }
    public float HorzRaySpacing { get; private set; }
    public float VertRaySpacing { get; private set; }
    public bool DrawRays { get; private set; } = false;
    public BoxCollider2D BoxCollider { get; private set; }
    public CollisionInfo Collisions { get; private set; }
    public List<Vector3Int> AdjacentTilePositions => GetAdjacentTilePositions();
    public List<Vector3Int> CellsOccupying => GetCellsOccupying();

    private Vector3 originalDeltaPos;
    private List<string> previousTilesOverlapping = new List<string>();
    private RaycastOrigins raycastOrigins;
    private bool aboveCollision, belowCollision, leftCollision, rightCollision;
    private PlayerMovement playerMovement;
    private EventManager eventManager;
    private Grid grid;
    private int playerDimension;
    private HandleTiles seperateTiles;


    private void Awake() {
        BoxCollider = GetComponent<BoxCollider2D>();
        playerMovement = GetComponent<PlayerMovement>();
        eventManager = FindObjectOfType<EventManager>();
        grid = GameObject.Find("Grid").GetComponent<Grid>();
        seperateTiles = FindObjectOfType<HandleTiles>();
    }

    private void Start() {
        playerDimension = playerMovement.PlayerDimension;

        CollisionMask = LayerMask.GetMask(LayerMask.LayerToName(BlockDimensionLayers[playerDimension]));
        CollisionMask |= LayerMask.GetMask(LayerMask.LayerToName(blocksLayer));

        Bounds bounds = GetBounds();
        HorzRaySpacing = bounds.size.y / (HorzRays - 1);
        VertRaySpacing = bounds.size.x / (VertRays - 1);
    }

    public Bounds GetBounds(bool trueBounds = false) {
        Bounds bounds = BoxCollider.bounds;
        if (!trueBounds) {
            bounds.Expand(skinWidth * -2f);
        }
        return bounds;
    }


    public void Move(Vector3 deltaPos) {
        Bounds bounds = GetBounds();
        raycastOrigins = new RaycastOrigins(bounds, TopInset, BottomInset, HorzInset);

        CollisionInfo oldCollisions = Collisions;
        aboveCollision = belowCollision = leftCollision = rightCollision = false;
        originalDeltaPos = deltaPos;

        if (deltaPos.x != 0) {
            var horzCollisionInfo = HorzCollisions(ref deltaPos);
            VertNudge(ref deltaPos, horzCollisionInfo);
        }
        if (deltaPos.y != 0) {
            var vertCollisionInfo = VertCollisions(ref deltaPos);
            HorzNudge(ref deltaPos, vertCollisionInfo);
        }

        Collisions = new CollisionInfo(aboveCollision, belowCollision, leftCollision, rightCollision);
        transform.Translate(deltaPos);

        HandleTileCollisions();
        seperateTiles.UpdateGroundTiles(playerDimension);

        // one frame delta y is for case where player lets go of wall at ground-level
        if (Collisions.below && !oldCollisions.below && originalDeltaPos.y < playerMovement.OneFrameDeltaY) {
            FindObjectOfType<EventManager>().playerLanded?.Invoke(playerDimension);
        }
    }


    private (int, float, bool, bool) HorzCollisions(ref Vector3 deltaPos) {
        int xDir = (int)Mathf.Sign(deltaPos.x);
        float rayLength = Mathf.Abs(deltaPos.x) + skinWidth;

        bool bottomEdgeCollision = false;
        bool topEdgeCollision = false;

        for (int i = 0; i < HorzRays; i++) {
            Vector2 rayOrigin = (xDir == -1) ? raycastOrigins.bottomLeft : raycastOrigins.bottomRight;
            rayOrigin += Vector2.up * (HorzRaySpacing * i);
            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.right * xDir, rayLength, CollisionMask);

            if (DrawRays) {
                Debug.DrawRay(rayOrigin, Vector2.right * xDir * rayLength, Color.red);
            }

            if (hit) {
                bottomEdgeCollision |= i == 0;
                topEdgeCollision |= i == VertRays - 1;

                // move player to be flush with object hit
                deltaPos.x = (hit.distance - skinWidth) * xDir;
                rayLength = hit.distance;

                leftCollision = xDir == -1;
                rightCollision = xDir == 1;
            }
        }

        return (xDir, rayLength, bottomEdgeCollision, topEdgeCollision);
    }

    private (int, float, bool, bool) VertCollisions(ref Vector3 deltaPos) {
        int yDir = (int)Mathf.Sign(deltaPos.y);
        float rayLength = Mathf.Abs(deltaPos.y) + skinWidth;

        bool leftEdgeCollision = false;
        bool rightEdgeCollision = false;

        for (int i = 0; i < VertRays; i++) {
            Vector2 rayOrigin = (yDir == -1) ? raycastOrigins.bottomLeft : raycastOrigins.topLeft;
            rayOrigin += Vector2.right * (VertRaySpacing * i + deltaPos.x);
            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.up * yDir, rayLength, CollisionMask);

            if (DrawRays) {
                Debug.DrawRay(rayOrigin, Vector2.up * yDir * rayLength * 10f, Color.red);
            }

            if (hit) {
                leftEdgeCollision |= i == 0;
                rightEdgeCollision |= i == VertRays - 1;

                deltaPos.y = (hit.distance - skinWidth) * yDir;
                rayLength = hit.distance;

                belowCollision = yDir == -1;
                aboveCollision = yDir == 1;
            }
        }

        return (yDir, rayLength, leftEdgeCollision, rightEdgeCollision);
    }


    // nudge player horizontally into position if they barely clip walls above or below them
    private void HorzNudge(ref Vector3 deltaPos, (int, float, bool, bool) vertCollisionInfo) {
        var (yDir, rayLength, leftEdgeCollision, rightEdgeCollision) = vertCollisionInfo;

        // inner rays determine how much of the player can clip a wall before they are no longer nudged
        Vector2 innerLeftRayOrigin = (yDir == -1) ? raycastOrigins.innerVertBottomLeft : raycastOrigins.innerVertTopLeft;
        Vector2 innerRightRayOrigin = (yDir == -1) ? raycastOrigins.innerVertBottomRight : raycastOrigins.innerVertTopRight;
        RaycastHit2D hitInnerLeft = Physics2D.Raycast(innerLeftRayOrigin, Vector2.up * yDir, rayLength + skinWidth, CollisionMask);
        RaycastHit2D hitInnerRight = Physics2D.Raycast(innerRightRayOrigin, Vector2.up * yDir, rayLength + skinWidth, CollisionMask);

        if (DrawRays) {
            Debug.DrawRay(innerLeftRayOrigin, Vector2.up * yDir * rayLength * 10f, Color.green);
            Debug.DrawRay(innerRightRayOrigin, Vector2.up * yDir * rayLength * 10f, Color.green);
        }

        float nudgeDir;
        // nudging direction set to left
        if (rightEdgeCollision && !leftEdgeCollision && !(hitInnerLeft || hitInnerRight) && System.Math.Sign(originalDeltaPos.x) != 1) {
            nudgeDir = -1;
        // nudging direction set to right
        } else if (leftEdgeCollision && !rightEdgeCollision && !(hitInnerLeft || hitInnerRight) && System.Math.Sign(originalDeltaPos.x) != -1) {
            nudgeDir = 1;
        } else { return; }

        float inset = (yDir == -1) ? BottomInset : TopInset;
        // calculating nudge distance
        Vector2 rayOrigin = (nudgeDir == -1) ? innerRightRayOrigin : innerLeftRayOrigin;
        RaycastHit2D hit = Physics2D.Raycast(rayOrigin + Vector2.up * yDir * (rayLength + skinWidth), Vector2.right * -nudgeDir, float.MaxValue, CollisionMask);
        // divide skinWidth by 2 because otherwise it nudges the player too far off the block
        float amountOnBlock = inset + skinWidth/2 - hit.distance;

        // nudging
        if (Mathf.Abs(originalDeltaPos.x) < amountOnBlock) {
            // calculate width of the hole the player is nudged into
            RaycastHit2D oppositeHit = Physics2D.Raycast(rayOrigin + Vector2.up * yDir * (rayLength + skinWidth), Vector2.right * nudgeDir, float.MaxValue, CollisionMask);
            int holeWidth = (int)Mathf.Round(oppositeHit.distance + hit.distance);
            // decide whether the player is nudged onto or off of the platform
            bool nudgeOffBlock = (yDir == 1 || Math.Sign(originalDeltaPos.x) == nudgeDir || holeWidth == 1 && originalDeltaPos.y == 0 || Mathf.Abs(originalDeltaPos.y) > MinNudgeDeltaY);
            deltaPos.x = nudgeOffBlock ? amountOnBlock * nudgeDir : (BottomInset + skinWidth * 2f - amountOnBlock) * -nudgeDir;
            // keep vertical momentum
            deltaPos.y = originalDeltaPos.y;
            aboveCollision &= yDir != 1;
            belowCollision &= yDir != -1;
            // recheck collisions since momentum is kept
            VertCollisions(ref deltaPos);
            HorzCollisions(ref deltaPos);
        }
    }

    // same process as HorzNudge
    private void VertNudge(ref Vector3 deltaPos, (int, float, bool, bool) horzCollisionInfo) {
        var (xDir, rayLength, bottomEdgeCollision, topEdgeCollision) = horzCollisionInfo;

        Vector2 innerBottomRayOrigin = (xDir == -1) ? raycastOrigins.innerHorzBottomLeft : raycastOrigins.innerHorzBottomRight;
        Vector2 innerTopRayOrigin = (xDir == -1) ? raycastOrigins.innerHorzTopLeft : raycastOrigins.innerHorzTopRight;
        RaycastHit2D hitInnerBottom = Physics2D.Raycast(innerBottomRayOrigin, Vector2.right * xDir, rayLength + skinWidth, CollisionMask);
        RaycastHit2D hitInnerTop = Physics2D.Raycast(innerTopRayOrigin, Vector2.right * xDir, rayLength + skinWidth, CollisionMask);

        if (DrawRays) {
            Debug.DrawRay(innerBottomRayOrigin, Vector2.right * xDir * rayLength, Color.green);
            Debug.DrawRay(innerTopRayOrigin, Vector2.right * xDir * rayLength, Color.green);
        }

        float nudgeDir;
        // nudging direction set to down
        if (topEdgeCollision && !bottomEdgeCollision && !(hitInnerBottom || hitInnerTop) && System.Math.Sign(originalDeltaPos.y) != 1) {
            nudgeDir = -1;
        // nudging direction set to up
        } else if (bottomEdgeCollision && !topEdgeCollision && !(hitInnerBottom || hitInnerTop) && System.Math.Sign(originalDeltaPos.y) != -1) {
            nudgeDir = 1;
        } else { return; }

        Vector2 rayOrigin = nudgeDir == -1 ? innerTopRayOrigin : innerBottomRayOrigin;
        RaycastHit2D hit = Physics2D.Raycast(rayOrigin + Vector2.right * xDir * (rayLength + skinWidth), Vector2.up * -nudgeDir, float.MaxValue, CollisionMask);
        float amountOnBlock = HorzInset + skinWidth - hit.distance;

        if (Mathf.Abs(originalDeltaPos.y) < amountOnBlock) {
            deltaPos.y = amountOnBlock * nudgeDir;
            // keep horizontal momentum
            deltaPos.x = originalDeltaPos.x;
            rightCollision &= xDir != 1;
            leftCollision &= xDir != -1;
            // recheck collisions
            HorzCollisions(ref deltaPos);
        }
    }


    private List<RaycastHit2D> GetHitsInDirection(Vector2 dir, int dimension, float rayLength = skinWidth * 1.5f, int rayCount = defaultRayCount, float drawRayScale = 0f) {
        List<RaycastHit2D> hits = new List<RaycastHit2D>();
        Bounds bounds = GetBounds();
        RaycastOrigins raycastOrigins = new RaycastOrigins(bounds, 0f);

        LayerMask collisionMask = LayerMask.GetMask(LayerMask.LayerToName(BlockDimensionLayers[dimension]));
        collisionMask |= LayerMask.GetMask(LayerMask.LayerToName(blocksLayer));

        if (dir == Vector2.left || dir == Vector2.right) {
            float raySpacing = bounds.size.y / (rayCount - 1);
            for (int i = 0; i < rayCount; i++) {
                Vector2 rayOrigin = (dir == Vector2.left) ? raycastOrigins.bottomLeft : raycastOrigins.bottomRight;
                rayOrigin += Vector2.up * (raySpacing * i);
                RaycastHit2D hit = Physics2D.Raycast(rayOrigin, dir, rayLength, collisionMask);
                if (hit) {
                    hits.Add(hit);
                }

                if (drawRayScale != 0f) {
                    Debug.DrawRay(rayOrigin, dir * rayLength * drawRayScale, Color.green);
                }
            }

        } else if (dir == Vector2.up || dir == Vector2.down) {
            float raySpacing = bounds.size.x / (rayCount - 1);
            for (int i = 0; i < rayCount; i++) {
                Vector2 rayOrigin = (dir == Vector2.down) ? raycastOrigins.bottomLeft : raycastOrigins.topLeft;
                rayOrigin += Vector2.right * (raySpacing * i);
                RaycastHit2D hit = Physics2D.Raycast(rayOrigin, dir, rayLength, collisionMask);
                if (hit) {
                    hits.Add(hit);
                }

                if (drawRayScale != 0f) {
                    Debug.DrawRay(rayOrigin, dir * rayLength * drawRayScale, Color.green);
                }
            }

        } else if (dir != Vector2.zero) {
            throw new ArgumentException("Controller2D.TestCollision: \"dir\" paramter invalid.");
        }

        return hits;
    }

    public bool TestCollision(Vector2 dir, int dimension, float rayLength = skinWidth * 1.5f, int rayCount = defaultRayCount, float drawRayScale = 0f) {
        List<RaycastHit2D> hits = GetHitsInDirection(dir, dimension, rayLength, rayCount, drawRayScale);
        foreach (var hit in hits) {
            if (hit) {
                return true;
            }
        }
        return false;
    }

    public bool[] TestCollision(Vector2 dir, int dimension, float inset, float rayLength = skinWidth * 1.5f, float drawRayScale = 0f) {
        bool[] hits = { false, false, false, false };
        Bounds bounds = GetBounds();
        RaycastOrigins raycastOrigins = new RaycastOrigins(bounds, inset);

        LayerMask collisionMask = LayerMask.GetMask(LayerMask.LayerToName(BlockDimensionLayers[dimension]));
        collisionMask |= LayerMask.GetMask(LayerMask.LayerToName(blocksLayer));

        List<Vector2> raysCasting = new List<Vector2>();
        if (dir == Vector2.up) {
            raysCasting.AddRange(new Vector2[] { raycastOrigins.topLeft, raycastOrigins.innerVertTopLeft, raycastOrigins.innerVertTopRight, raycastOrigins.topRight });
        } else if (dir == Vector2.down) {
            raysCasting.AddRange(new Vector2[] { raycastOrigins.bottomLeft, raycastOrigins.innerVertBottomLeft, raycastOrigins.innerVertBottomRight, raycastOrigins.bottomRight });
        } else if (dir == Vector2.right) {
            raysCasting.AddRange(new Vector2[] { raycastOrigins.topRight, raycastOrigins.innerHorzTopRight, raycastOrigins.innerHorzBottomRight, raycastOrigins.bottomRight });
        } else if (dir == Vector2.left) {
            raysCasting.AddRange(new Vector2[] { raycastOrigins.topLeft, raycastOrigins.innerHorzTopLeft, raycastOrigins.innerHorzBottomLeft, raycastOrigins.bottomLeft });
        } else if (dir != Vector2.zero) {
            throw new ArgumentException("Controller2D.TestCollision: \"dir\" paramter invalid.");
        }

        if (raysCasting.Count == 4) {
            for (int i = 0; i < raysCasting.Count; i++){
                hits[i] = Physics2D.Raycast(raysCasting[i], dir, rayLength, collisionMask);
                if (drawRayScale != 0f) {
                    Debug.DrawRay(raysCasting[i], dir * rayLength * drawRayScale, Color.green);
                }
            }
        }

        return hits;
    }

    public float DistanceTo(Vector2 dir, int dimension, int rayCount = defaultRayCount) {
        float distance = float.MaxValue;
        List<RaycastHit2D> hits = GetHitsInDirection(dir, dimension, float.MaxValue, rayCount);
        foreach (var hit in hits) {
            distance = Mathf.Min(distance, hit.distance);
        }
        return distance;
    }


    private void HandleTileCollisions() {
        List<string> tilesOverlapping = GetTilesOverlapping();
        foreach (var tile in tilesOverlapping) {
            if (!previousTilesOverlapping.Contains(tile)) {
                switch (tile) {
                    case "kill_area":
                        eventManager.playerDeath?.Invoke();
                        break;
                }
            }
        }
        previousTilesOverlapping = tilesOverlapping;
    }

    public List<string> GetTilesInDirection(Vector2 dir, int dimension = 0, float rayLength = skinWidth * 1.5f, int rayCount = defaultRayCount, float drawRayScale = 0f, string tilemapObjectName = "SolidSensors") {
        GameObject tilemapObject = GameObject.Find(tilemapObjectName);
        Tilemap tilemap = tilemapObject.GetComponent<Tilemap>();
        // 0 dimension means collision in both dimensions
        List<RaycastHit2D> hits = GetHitsInDirection(dir, dimension, rayLength, rayCount, drawRayScale);

        List<string> tiles = new List<string>();
        foreach (var hit in hits) {
            string tile = GetTileAtPosition((Vector3)(hit.point + dir * skinWidth), tilemap);
            if (tile != null) {
                tiles.Add(tile);
            }
        }
        tiles = tiles.Distinct().ToList();

        return tiles;
    }

    public List<string> GetTilesOverlapping(string tilemapObjectName = "Sensors") {
        GameObject tilemapObject = GameObject.Find(tilemapObjectName);
        Tilemap tilemap = tilemapObject.GetComponent<Tilemap>();

        List<string> tiles = new List<string>();
        Vector2[] positions = { raycastOrigins.topLeft, raycastOrigins.topRight, raycastOrigins.bottomLeft, raycastOrigins.bottomRight, transform.position };
        foreach (var position in positions) {
            string tile = GetTileAtPosition(position, tilemap);
            if (tile != null) {
                tiles.Add(tile);
            }
        }
        tiles = tiles.Distinct().ToList();

        return tiles;
    }

    private List<Vector3Int> GetCellsOccupying() {
        Bounds bounds = GetBounds();
        RaycastOrigins raycastOrigins = new RaycastOrigins(bounds, TopInset, BottomInset, HorzInset);
        List<Vector3Int> cellPositions = new List<Vector3Int>();

        Vector2[] positions = { raycastOrigins.topLeft, raycastOrigins.topRight, raycastOrigins.bottomLeft, raycastOrigins.bottomRight, raycastOrigins.innerHorzBottomLeft, raycastOrigins.innerHorzBottomRight, raycastOrigins.innerHorzTopLeft, raycastOrigins.innerHorzTopRight, transform.position };
        foreach (var position in positions) {
            Vector3Int cellPosition = grid.WorldToCell(position);
            cellPositions.Add(cellPosition);
        }
        cellPositions = cellPositions.Distinct().ToList();

        return cellPositions;
    }

    public string GetTileAtPosition(Vector3 position, Tilemap tilemap) {
        Vector3Int cellPos = grid.WorldToCell(position);
        return tilemap.GetTile(cellPos)?.name;
    }

    private List<Vector3Int> GetAdjacentTilePositions() {
        List<Vector3Int> tilePositions = new List<Vector3Int>();

        /*List<Vector2> directions = new List<Vector2>();
        if (Collisions.above) {
            directions.Add(Vector2.up);
        }
        if (Collisions.below) {
            directions.Add(Vector2.down);
        }
        if (Collisions.left) {
            directions.Add(Vector2.left);
        }
        if (Collisions.right) {
            directions.Add(Vector2.right);
        }*/
        List<Vector2> directions = new List<Vector2>() { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

        foreach (var dir in directions){
            List<RaycastHit2D> hits = GetHitsInDirection(dir, playerDimension);
            foreach (var hit in hits) {
                Vector3Int cellPos = grid.WorldToCell(hit.point + dir * skinWidth);
                tilePositions.Add(cellPos);
            }
        }
        tilePositions = tilePositions.Distinct().ToList();

        return tilePositions;
    }


    public struct RaycastOrigins {
        public readonly Vector2 topLeft;
        public readonly Vector2 topRight;
        public readonly Vector2 bottomLeft;
        public readonly Vector2 bottomRight;

        public readonly Vector2 innerVertTopLeft;
        public readonly Vector2 innerVertTopRight;
        public readonly Vector2 innerVertBottomLeft;
        public readonly Vector2 innerVertBottomRight;
        public readonly Vector2 innerHorzTopLeft;
        public readonly Vector2 innerHorzTopRight;
        public readonly Vector2 innerHorzBottomLeft;
        public readonly Vector2 innerHorzBottomRight;

        public RaycastOrigins(Bounds bounds, float inset) {
            bottomLeft = new Vector2(bounds.min.x, bounds.min.y);
            bottomRight = new Vector2(bounds.max.x, bounds.min.y);
            topLeft = new Vector2(bounds.min.x, bounds.max.y);
            topRight = new Vector2(bounds.max.x, bounds.max.y);

            // inset raycast origins are used to calculate sliding past edges that are barely clipped
            innerVertTopLeft = topLeft + Vector2.right * inset;
            innerVertTopRight = topRight + Vector2.left * inset;
            innerVertBottomLeft = bottomLeft + Vector2.right * inset;
            innerVertBottomRight = bottomRight + Vector2.left * inset;
            innerHorzTopLeft = topLeft + Vector2.down * inset;
            innerHorzTopRight = topRight + Vector2.down * inset;
            innerHorzBottomLeft = bottomLeft + Vector2.up * inset;
            innerHorzBottomRight = bottomRight + Vector2.up * inset;
        }

        public RaycastOrigins(Bounds bounds, float topInset, float bottomInset, float horzInset) {
            bottomLeft = new Vector2(bounds.min.x, bounds.min.y);
            bottomRight = new Vector2(bounds.max.x, bounds.min.y);
            topLeft = new Vector2(bounds.min.x, bounds.max.y);
            topRight = new Vector2(bounds.max.x, bounds.max.y);

            // inset raycast origins are used to calculate sliding past edges that are barely clipped
            innerVertTopLeft = topLeft + Vector2.right * topInset;
            innerVertTopRight = topRight + Vector2.left * topInset;
            innerVertBottomLeft = bottomLeft + Vector2.right * bottomInset;
            innerVertBottomRight = bottomRight + Vector2.left * bottomInset;
            innerHorzTopLeft = topLeft + Vector2.down * horzInset;
            innerHorzTopRight = topRight + Vector2.down * horzInset;
            innerHorzBottomLeft = bottomLeft + Vector2.up * horzInset;
            innerHorzBottomRight = bottomRight + Vector2.up * horzInset;
        }
    }

    public struct CollisionInfo {
        public readonly bool above;
        public readonly bool below;
        public readonly bool left;
        public readonly bool right;

        public CollisionInfo(bool above, bool below, bool left, bool right) {
            this.above = above;
            this.below = below;
            this.left = left;
            this.right = right;
        }
    }
}
