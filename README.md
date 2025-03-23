This is a custom mouse acceleration program I slapped together with the help of Grok & Deepseek that showcases how I feel acceleration should be implemented for gyro gaming. I basically reverse-engineered Raw Accels curves and set up a more intuitive UI specifically for gyro. I call it "Gyro Gravity" because sensitivity dictates how "heavy" your gyro aim feels in-game. Since most gamers react to acceleration with a predisposed bias to turn it off, I think it’d be a good idea not to call this acceleration if implemented in a game. Steadying is already a pretty good name too.

BUG: I literally don’t know how to code, so this thing is held together with duct tape. Disabling the Limit for the linear curve seems to cause an incorrect sensitivity response and I have no clue why.

General Issue: I wanted this to work with Steam Input and Joyshockmapper but I ran into an unfixable issue. Mouse output gets properly replaced when you are on desktop but as soon as you are in a game, Steam Input and Joyshockmappers output do not get fully blocked. This results in the base 1:1 sensitivity getting added to the modified Gyro Gravity output. This is probably has something to do with inputs happening at the Raw Input level.

USE AT YOUR OWN RISK!: I only made this and am sharing it so actual devs can implement something like this natively. I have no clue if this will be flagged by anti-cheat, so I have to give an obligatory warning.
-
Gyro Gravity Breakdown:
-
Yaw X and Pitch Y have been separated to allow for full accurate control of a user's gyro ratio. By default, you only need to adjust Yaw settings for 1:1 sensitivity.

Synchronize Curves ensure that the same curve is used for X and Y. If you want to, you can turn it off to mix and match curves. I wouldn’t actually suggest this at all but I decided to add the option anyways cause why not.

Synchronize Settings lets you enable/disable separate X and Y settings. So instead of juggling awkward base ratio settings, I feel it would be better to let users set exact ratios this way.

Dots Per 360: Just like Steam Input, place your Dots Per 360 here for proper calibration. This will scale everything so 1 Count Per Second is the same as 1 Degree Per Second.

Curve Types:
-
Natural: This curve is basically “true linear”. It is the most simple curve that gives you the most control just like linear but better. This provides an extremely smooth transition between sensitivities and I would highly recommend this as a default.

Linear: This is typically what people think of when they say Accel feels bad. While simple logic says the quickest path is a straight line, in this case, a straight line is not the smoothest.

Power: I’m fairly certain this is what Steam Inputs old accel settings use. It requires an extra setting for an Exponent and is more finicky to set up because of this. Unlike Natural though, a power curve could allow your sensitivity to increase infinitely past a certain point depending on your Exponent. An Exponent of .05 is what Valve uses in their games.

Sigmoid: If you plan on using an Offset, this is the only curve I’d recommend doing that with. This curve is similar to Natural but is visually much smoother. It’s much more computationally heavy than the other curves though, so I don’t know how much I suggest using this curve.

Gain:
-
This is a feature from Raw Accel that I tried my best to replicate (I might be doing it a little bit differently). Essentially, this determines if the fundamental curve is used directly as a Sensitivity Curve or if it's treated as a Velocity Curve.

If you measure the Velocity of any Sensitivity Curve, you can produce a Velocity curve that represents what the relative change in sensitivity will feel like. So with Gain on, we can then pretend that our Original Sensitivity Curve IS the Velocity Curve. By performing some reverse calculations, we can create a New Sensitivity Curve which will produce a New Velocity Curve that resembles our Original Sensitivity Curve. So now, your Sensitivity Curve will "feel" like how it originally looked.

Other Settings:
-
Enable Limit: This is only available for Linear and Power. Does what it says, it caps your sensitivity at a target.

Target Gyro Ratio: How high you want your sensitivity.

Mirror Sense: A big problem I have with Steadying is that your sensitivity starts at 0. I am not a fan of this as it destroys micro precision. This may seem ironic since low sensitivity is good for precision. To understand why 0 is bad, I need to explain scaling. To make your sensitivity twice as light relative to 1:1 reality, you multiply 2x. To make it twice as heavy though, you multiply .5x. When you multiply by 0, you are literally making your gyro INFINITLY heavier. So my suggestion is this, Mirror Sense. When enabled, this will automatically apply a lower sensitivity based on your Target Gyro Ratio. For example, if you use a 4:1 ratio, then your Base Gyro is now 1:4 or .25x.

Base Gyro Ratio: How low/heavy you want the start of your sensitivity to feel.

Target Degrees Per Second: At what speed you want to reach your Target Gyro Ratio.

Target Degrees Recommendation: This is a very simple linear-based calculation I came up with for Target Degrees. Using 1:1 reality as a reference point, there is a max speed at which the distance traveled is the same as 1:1 for any given Target Gyro Ratio. For example, 4:1 will intercept 1:1 at 4 Degrees/S with a Target Degree of 16. In other words, your 4x Ratio will travel the same distance as 1:1 at 4x Speed. Any Target Degree further will actually be “heavier” than 1:1. Anything lower will feel lighter. I only suggest staying within a range because I calculate this value with Target Ratio^2. Feel free to use any value you want, all the way down to 0.

Offset: I flat-out don’t recommend using this (except on Sigmoid). It delays when your sensitivity begins to increase.

Graphs:
-
Sensitivity: A very straightforward representation of what your sensitivity actually is.

Jolt: This represents what your sensitivity will feel like. What you want is a smooth transition from high to low. Imagine you’re are a skateboarder, would you rather have a nice smooth ramp to roll down or a cliff with a hard stop?

Velocity: This also kinda shows what your sensitivity will feel like. You can also use this to see at which point your sensitivity feels 1:1 (assuming you use a low base gyro).

