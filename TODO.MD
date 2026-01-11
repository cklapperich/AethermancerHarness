8. Queryable state. dont always present full state. rpesent full state less often. when DO we present full state besides querying /state? 
how can we break /state down into semantic micro-queries? Ie /state/cherufe? OR should this be on the toolcalling side? 
toolcaling for state(cherfue) returns a call to /state that gets filtered by python code into the proper state? might be easier.

2. When calling startrun, waitrorready() doesnt work properly. Need to fix or add a manual http thread sleep when in watchable mode.

3. still no proper autoadvance on dialoguues for some things like monster shrines and 
