# KSPModDevInstaller
Small utility for when you want to setup a dev environment for an already existing KSP mod whose source code resides in a KSP repo and has a more or less "standard" structure (meaning it uses `.csproj` project files and has a GameData folder in the repo).
## How
It will ask you for the path to your KSP install, a local or remote git repository; it will then install the related mod from ckan, symlink the relevant contents of the repo with the GameData of the ksp install and automatically create .csproj.user files for every csproj in the solution.
Every step is optional, so you can skip it if you don't need it.
### Why
It's essentially a small script performing a few boring steps that I had to do every time I wanted to make a PR to a mod or try out a few changes.
After running it fully you should end up with every change you make in the repo automatically reflected in your KSP install (through the symlink) and no dependency issues (thanks to the `.csproj.user` file telling the compiler to get the dependencies from the install).
### Dependencies
- any version of KSP
- git
- ckan client

It needs to run next to the CKAN binary (ckan.exe on Windows), or to have it inside your PATH.
