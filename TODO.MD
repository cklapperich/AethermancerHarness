1. Fix bug with end of combat auto-advance.

2. Interactable overhaul: Always teleport to interactable, then interact with it. So much simpler. Might fix some bugs.

3. watchable-mode teleport: get game to play the map teleport animation. When on the map, you can click a POI and teleport to it with space bar.
Need some way of knowing when the animation is done? or maybe not. animation is relatively short, llms take a bit to process. 
IF no good way to detect animation done, add a 1 second delay, make it a bepinex config constant.
in 'watchable' mode, should use map teleport. Move all teleport related stuff to a separate file.

4. enhanced 'watchable' mode: always teleport first WITH animation. 
then interact. Even if /interact is chosen by the AI

5. Make dialogue options index based again.

6. Bug: Events not auto advancing to the choice point. example: lantern. If a dialogue option has only 1 choice, it must auto-advance after a short delay, or in watchable mode, a longer delay.

7. Also we need to strip HTML out of dialogue text

8. Queryable state. dont always present full state. rpesent full state less often. when DO we present full state besides querying /state? 
how can we break /state down into semantic micro-queries? Ie /state/cherufe? OR should this be on the toolcalling side? 
toolcaling for state(cherfue) returns a call to /state that gets filtered by python code into the proper state? might be easier.


10. care chest? interaction timeout, but then interaction is sucessful.

11. Interaction / Dialogue rework. REMOVE ALL TIMEOUTS, AND ALL SLEEP STATEMENTS. its always a hedge, an anti-pattern, a way to paper over not understanding how control flow or unity works.
rearchitect so that unity thread returns to http quickly. http can query state. based on state, main http thread can decide to try again. polling isnt that bad we can poll sometimes.

Bug #1 when you interact with aetherspring it does NOT wait for spring to be ready to return. we should just get like

      "Choices": [
        {
          "Name": "Equipment Boon",
          "Element": "Fire",
          "Tier": 1,
          "Description": "Equipment from Random Loot Drops have a 35% chance to have a higher tier.",
          "Effect": "EquipmentDrop"
        },
        {
          "Name": "Gold Boon",
          "Element": "Earth",
          "Tier": 1,
          "Description": "At the end of each combat: Gains 2 extra gold.",
          "Effect": "Gold"
        } 
      ],

Bug #2: when you pickup loot, (type:'Chest'), a confirm screen shows up. we dont detect this. we dont auto-advance past it.  

we need a more generic way of handling state, confirmations, waiting, and so on.

we need to clearly define what runs on Unity thread via main thread. See sleep-audit.md for a start on this work.