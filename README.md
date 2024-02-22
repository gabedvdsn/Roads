# Roads

Unity Version 2022.3.71f

This program generates road maps along multiple axes, including horizontal, vertical, and diagonal axes in 2D. My vision for this project is to eventually convert it into a dungeon generation tool in future projects. While this project is setup for a 2D environment, the logic remains the same for 3D development. I can see this project being adapted into future 3D projects in this way.

Generation depends on 2 coefficients:
- **generationDepth**
- **generationMagnitude**

Generation is performed iteratively, such that for each iteration (in range **generationDepth**) a new road will be added to each non-temrinal road segment. For as long as there are less than **generationDepth** * **generationMagnitude** roads in the system, this will continue. Generally, the number of roads in the system, *S*, is: **generationDepth** * **generationMagnitude** < *S* < **generationDepth** * **(generationMagnitude + 1)**.

Currently, future development plans include support for separation and linearity in generation. 

Separation refers to the distance between parallel road segments. Linearity refers to the tendency for a road segment to generate in the same direction as the segment it is being generated off of. 

------------

Note issues in sprite layouts. This issue may lie in the sprites themselves, which may be the case for some segments. Sprite overlap is causing some visual issues but does not affect generation, which is being done correctly. This can be shown by examining the underlying RoadSystem class (see RoadGeneration.cs in Assets/Scripts) as the map is generated. 

Furthermore, there are some critical issues when generating maps with both diagonal and square pieces, where some diagonal segments do not align correctly with other diagonal segments. This issue does not arise when only generating diagonal segments.
