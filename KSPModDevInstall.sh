#!/usr/bin/bash

function ask_yes_no {
    read -rep "$1 [Y/n]: "
    case $(echo $REPLY | tr '[A-Z]' '[a-z]') in
        n*|N* ) echo "no" ;;
        * )   echo "yes" ;;
    esac
}

function ask_repo_url {
    read -rep "Enter the mod repo url: " input

    regex='((git|ssh|https?)|(git@[[:alnum:]\.]+)):(\/\/)?([[:alnum:]\.@\/~:-]+)\/([[:alnum:]\.@~:-]+)(\.git)?\/?'
    while [[ ! $input =~ $regex ]]
    do
        echo invalid
        read -rep "Enter the mod repo url: " input
    done

    name="${BASH_REMATCH[6]}"
    
    if [[ $(ask_yes_no "Repo $name found. It will be cloned at '$(pwd)/$name'. Continue? ") = "no" ]]; then
        exit 1
    fi

    git clone ${BASH_REMATCH[0]}
    repo_path=$(realpath "./$name") 
}

function ask_repo_path() {
    read -rep "Enter the git repo path: " input

    while [[ ! $(git -C $input rev-parse 2>/dev/null; echo $?) = 0 ]]
    do
        echo "Repo path not valid"
        read -rep "Enter the git repo path: " input
    done
    
    repo_path=$(realpath $input)   
}

function gamedata_symlink() {
    mod_dirs_gd=($mod_dirs)
    
    echo "NOTE: symlinking will delete and replace the old mod folder in your KSP install with a link to the same folder in the repo."
    for mod_dir in ${mod_dirs_gd[@]}
    do
        mod_dirname=$(basename $mod_dir)
        if [[ $(ask_yes_no "Do you want to symlink GameData/$mod_dirname?") = "yes" ]]; then
            rm -rf "$KSPDEVPATH/GameData/$mod_dirname"
            ln -s $mod_dir "$KSPDEVPATH/GameData/$mod_dirname"
            if [[ $? = 0 ]]; then
                echo "symlink created from '$mod_dir' to '$KSPDEVPATH/GameData/$mod_dirname'."
            fi
        fi
    done
}

function create_csproj_user {
    echo -e '<?xml version="1.0" encoding="utf-8"?>' > $2
    echo -e '<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">' >> $2
    echo -e '  <PropertyGroup>' >> $2

    if [[ -d "$KSPDEVPATH/KSP_x64_Data" ]]; then
        refpaths='    '"<ReferencePath>$KSPDEVPATH/KSP_x64_Data/Managed</ReferencePath>\n"
    else
        refpaths='    '"<ReferencePath>$KSPDEVPATH/KSP_Data/Managed</ReferencePath>\n"
    fi
    
    # transform dllpaths into array
    readarray -t <<<"$3"
    for dllpath in "${MAPFILE[@]}"
    do
        # test if result is empty or contains only whitespace, then get the path
        if [[ ! -z "${dllpath// }" ]]; then
            path="$(dirname "$dllpath")"
            echo "$path"
        fi

        # if the new path is not already in refpaths
        # grep -cim1 returns 0 if not found, 1 if found
        if  [[ ($(echo "$refpaths" | grep -cim1 "$path") -eq 0) && !(-z "${dlldeps// }") ]]; then
            refpaths+='    '"<ReferencePath>$path</ReferencePath>\n"
        fi
    done

    # remove trailing newline (-n) and print expanding newlines (-e)
    echo -e -n "$refpaths" >> $2
    echo -e '  </PropertyGroup>' >> $2
    echo -e '</Project>' >> $2
}

# get the KSP path
if [[ -z "${KSPDEVPATH}" ]]; then
    read -rep "Enter the path of your KSP install: " KSPDEVPATH

    while [[ ! -d $KSPDEVPATH ]]; do
        echo "Entered path is not valid"
        read -rep "Enter the path of your KSP install: " KSPDEVPATH
    done
fi
echo "KSP dev install found at '$KSPDEVPATH'"

# get the repo path or clone it
echo ""
if [[ $(ask_yes_no "Have you already cloned the repo?") = "yes" ]]; then
    ask_repo_path
else
    ask_repo_url
fi

# find the netkan and optionally install the mod
echo ""
mod_id=$(cat $(find $repo_path -name '*.netkan') | jq -r '.identifier')
echo -e "netkan found in repo: $mod_id\n"
echo "Do you want to install $mod_id and its dependencies in your dev install through CKAN?"
if [[ $(ask_yes_no "You can decline if you have already installed it.") = "yes" ]]
then
    ckan install --no-recommends --gamedir "$KSPDEVPATH" $mod_id
fi

# find the gamedata in the repo and optionally symlink its contents
echo ""
mod_dirs=$(ls -1 -d "$(find $repo_path -name 'GameData')"/*)
mod_dirs_names=$(echo $mod_dirs | xargs -n1 basename | paste -sd ',')
if [[ -n $mod_dirs ]]
then
    echo "Gamedata found in the repo: found folder(s) $mod_dirs_names"
    gamedata_symlink
else 
    echo "no GameData found in the repo. Cannot symlink."
fi

# look for .csproj files and create .csproj.users with the dependencies
echo ""
mod_csprojs=$(find $repo_path -name '*.csproj')

# return if no .csproj is found
if [[ -z $mod_csprojs ]]; then
    echo "No .csproj files found in the repo."
    exit 2
fi

echo "Found $(echo "$mod_csprojs" | wc -l) .csproj file(s) in the mod repo."
if [[ $(ask_yes_no "Do you want to add corresponding .csproj.user files referencing dependencies to your install?") = "yes" ]]
then
    # foreach .csproj
    while IFS= read -r csprojfile; do 
        echo ""
        echo "creating $(basename $csprojfile).user"

        # get dependencies from .csproj and find the dlls in the KSP install
        dlldeps="$(pcregrep -o1 '<Reference Include="(.*)"' $csprojfile | xargs -I{} find "$KSPDEVPATH/GameData" -iname '{}.dll')"

        create_csproj_user "$csprojfile" "$csprojfile.user" "$dlldeps"
    done <<< "$mod_csprojs"
fi

