The name matching algorithm should work as follows

Go through all files and folder


Example scenario:

./[Ember] Jujutsu Kaisen 58.mkv
./Jujutsu Kaisen 59 [SubsPlease].mkv
./Jujutsu Kaisen/Jujutsu Kaisen 60.mkv
./Taxi Driver.mkv
./Dark Matter 1080p x265 ELiTE[EZTVx to] S01E07.mkv
./Dark Matter/Dark Matter 1080p x265 ELiTE[EZTVx to] S01E08.mkv
./Dark Matter/Season 01/Dark Matter S01E06.mkv

Should result into
./Jujutsu Kaisen/Season 01/Jujustu Kaisen 58.mkv
./Jujutsu Kaisen/Season 01/Jujustu Kaisen 59.mkv
./Jujutsu Kaisen/Season 01/Jujustu Kaisen 60.mkv
./Taxi Driver/Taxi Driver.mkv
./Dark Matter/Season 01/Dark Matter S01E06.mkv
./Dark Matter/Season 01/Dark Matter S01E07.mkv
./Dark Matter/Season 01/Dark Matter S01E08.mkv

So there needs to be a smart name matching algorithm which
- Removes anything between tags like []()
- Removes anything related to 1080p 720p
- Saves S01E8 as season and episode tracing
- If there is no S01E08 tag but the name matching algorithm has matched multiple files together
  - If the filename ends with a number like 58 then thats the epsideo number and just put it in season 01
  - If there is no detected episode number then just group them together and based on file name alphabetical order add episode numbers to the end
- Now try to find close name matches. If matches for like 80% then it is a group together
Now Make objects from the matching results:
MediaObject
- Name
- Type: Movie/Show
- Movie Path string? (Set if movie)
- Seasons: Season (Set if show)
	- Season Number
	- Episode paths list<string>
  

Can you make this algorithm