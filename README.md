# Precision Platformer Prototype

A demo level for a puzzle-precision platformer. The core mechanic is that you control two different characters who can only touch their own color of blocks.
However, by moving off a block you change it's color, so the two characters must work together to clear the level.

Demo video:

https://user-images.githubusercontent.com/82133480/156235180-b01d4b80-6a6a-427b-9157-7afa55b63764.mov

The physics and collisions were implimented from scratch for more granular control. This allowed for the addition of platforming mechanics that would have been inconvenient otherwise, such as:
- Quick turnarounds (found in PlayerMovement.UpdateXVelocity)
- Short hopping (found in PlayerMovement.UpdateYVelocity)
- Pixel-perfect collision snapping (found in PlayerCollision.HorzNudge and PlayerCollision.VertNudge)

All player movement code is included. These scripts act as a foundation for any 2D platformer's movement. They include features like wall climbing that weren't included in the demo level. Other supplimental scripts such as the particle and tile managers are not included since they're highly dependent on Unity's editor settings and local assets.

A build of the level is included.

Controls:
- Red Character: WASD
- Blue Character: Arrow Keys
