# Winspod #

Following on from the old days of PGPlus+, NUTS etc, Winspod has been created to carry on the work started by these pioneering programmers.

Based loosely on the ewtoo/pgplus+ command set, Winspod will provide most of the original features, plus have the ability to expand the services offered to users.

## History ##

After spending many years as a spod on MUDs such as Pern, Dominion, MBA4, Snowplains etc, the lack of any updates to the source code to take advantage of modern programming languages became a source of intrigue.

In 2000 the original Winspod code was born, written in VB6, and the command and feature set was created. Over the course of a year the code became mostly complete, but due to a lack of time the project stagnated and was never released to the public.

Many years on (2009), and with a renewed interest in the "old days", Winspod II crept into life, originally as just test socket server in C# for another project. Over the following months, Winspod II gained more and more functionality as time permitted, and today is about 75% complete against the original Winspod code.

## Current Status ##

As of the time of writing, Winspod II _works_ .. just about. It resides within a development environment at the moment, and needs many more hours work before it is even close to being stable enough for production. Much of the code needs re-factoring and tidying up, and some of the methods are far too cludgy for the authors liking, however like the proverbial phoenix, it is slowly but surely rising from the ashes!

As of 9th September 2010, the Winspod code will compile and run under Mono, allowing the code to be run on both Windows and Linux systems.

### Update - January 2014 ###

I finally found time to look into the annoying bug that would make Winspod crash out under Mono when a player quitted. I believe this to be fixed now, and have moved and updated the demo server listed below for people to test.

### Why? ###

There are those that have asked why this project is even alive, considering the plethora of social networking sites, chat rooms and instant messaging systems out there these days.

The honest answer? Purely for fun!

Yes, it might seem geeky and a little bit sad, however it is a project that I am having fun coding. I plan to get the system finished, but then life has a way of stopping the best laid plans. Will this ever be production ready? I don't know. Even if it does, would anyone be interested in using it? Another good question. Only time will tell I guess, but in the meantime I will continue to develop and improve the code as and when I can.

That said - if anyone is interested in this project in the slightest, please drop me a note and let me know. It would be great to find some other spods out there!

## Demo ##

A reasonably up-to-date build is running on the test server.

Connect :[Demo](telnet://homelands.paradigmsystems.co.uk:4000)

Webserver: [Demo](http://homelands.paradigmsystems.co.uk:4001)

**Windows users**

Due to a known bug in Windows telnet, output from the server will appear very screwy (i.e. no local echo after entering password). It can be somewhat fixed by following the instructions [here](http://www.zebedee.org/telnet_xp.html), or getting yourself a `*`nix shell account and telnetting from there.