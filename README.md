# Steam DOTA2 Client

[![highgroundvision MyGet Build Status](https://www.myget.org/BuildSource/Badge/highgroundvision?identifier=bcc46394-8ef3-41bd-b5b9-655e13bfbf21)](https://www.myget.org/feed/highgroundvision/package/nuget/HGV.Crystalys)

Uses [SteamKit2](https://github.com/SteamRE/SteamKit) to interoperate with Valve's Steam network. Its primay function is to download DOTA2 match replays. Other features maybe added in the future.

**Project Status:**

- Can download match replays. Yippy!
- Need to move some of the call backs into the request for the replay match so that the client can be used for other functions.
- Need to add Async support.

## Getting Started

To download match replays as byte arrays supply a valid username and password and match id to the clients constructor then call the DownloadReplay method.

You can now download this project as a NuGet package through [MyGet](https://www.myget.org/feed/highgroundvision/package/nuget/HGV.Crystalys). We may move these packages to [Nuget Gallery](https://www.nuget.org/) in the future. 

## License

GNUv2 see the LICENSE file.

## Help

If you have any questions, you can find us in the at [HGV Facebook Group](http://www.facebook.com/groups/1079291435444419/
)

## Authors and Acknowledgements

Crystalys is maintained and development by [HGV](http://www.highgroundvision.com), a leading Dota 2 data visualization and analysis web site. HGV's Team:

* [Jamie Webster](https://github.com/RGBKnights) 
* [Graham Clifford](https://github.com/gclifford)

Special thanks to the following people:

* [Ryan Stecker](https://github.com/VoiDeD) built steam kit [SteamKit2](https://github.com/SteamRE/SteamKit).
