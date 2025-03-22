This is a custom mouse acceleration program I slapped together with the help of Grok & Deepseek that showcases how I feel acceleration could be implemented for gaming. I basically reverse-engineered Raw Accels curves and set up a more intuitive UI specifically for gyro. I call it "Gyro Gravity" because sensitivity dictates how "heavy" your gyro aim/gun feels in game. Since most gamers react to acceleration with a predisposed bias to turn it off, I think it’d be a good idea not to call this acceleration if implemented in a game. Steadying is already a pretty good name too.

BUGS: I literally don’t know how to code, so this thing is held together with duct tape. Uncapping the linear curve seems to cause an incorrect sensitivity response and I have no clue why.

General Issue: I wanted this to work with Steam Input and Joyshockmapper but I ran into an unfixable issue. Mouse output gets properly replaced when you are on desktop but as soon as you are in a game, Steam Input and Joyshockmappers output do not get fully blocked. This results in the base 1:1 sensitivity getting added to the modified Gyro Gravity output.

USE AT YOUR OWN RISK!: I only made this and am sharing this so actual devs can implement something like this natively. I have no clue if this will be flagged by anti-cheat, so I have to give an obligatory warning.
