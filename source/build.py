import shutil
import os
import subprocess

def try_delete(file_path):
    shutil.rmtree(file_path, ignore_errors = True)

def run_command_generator(command):
    with subprocess.Popen(command, stdout=subprocess.PIPE, bufsize=1, universal_newlines=True) as p:
        for line in p.stdout:
            yield line 

    if p.returncode != 0:
        raise subprocess.CalledProcessError(p.returncode, command)

def run_command_sync(command):
    print(command)
    for output_line in run_command_generator(command):
        print(output_line, end="")

run_sync = run_command_sync

def copy_tree_if_modified(dest_dir, source_dir):

    if os.path.exists(dest_dir):
        modification_time_source = os.stat(source_dir).st_mtime
        modification_time_dest   = os.stat(dest_dir).st_mtime

        # Check if the source has changed, i.e. it is older than the destination
        if modification_time_dest >= modification_time_source:
            return

        # TODO: ?
        # Check if the files match
        # If they do not match, rewrite to the destination

        shutil.rmtree(dest_dir)

    shutil.copytree(source_dir, dest_dir)

def try_make_dir(path):
    os.makedirs(path, exist_ok = True)


NUGET_COPY_TO_PATH               = "destination"
NUGET_REPOSITORY_PATH            = ".nuget"
MSBUILD_INTERMEDIATE_OUTPUT_PATH = "obj"
MSBUILD_OUTPUT_PATH              = "bin"

# Clear all previous output
try_delete(NUGET_REPOSITORY_PATH)
try_delete(MSBUILD_INTERMEDIATE_OUTPUT_PATH)
try_delete(MSBUILD_OUTPUT_PATH)

# A list of projects to be compiled into nuget packages
# By convention, also their assembly names
nuget_projects = ["Kari.Generators", "Kari.Shared", "Kari"]

def copy_nuget_output(assembly_name):
    dest_dir   = os.path.join(NUGET_COPY_TO_PATH, assembly_name)
    source_dir = os.path.join(NUGET_REPOSITORY_PATH, assembly_name)
    copy_tree_if_modified(dest_dir, source_dir)
    
def copy_all_nuget_output():
    nuget_packages = os.listdir(NUGET_REPOSITORY_PATH)

    for item in nuget_packages:
        copy_nuget_output(item)

    print(f'Copied {len(nuget_packages)} assemblies from nuget repo folder')

try:
    # Invoke nuget packing commands
    for p in nuget_projects:
        run_sync(f'dotnet pack {p}')
    
    # TODO: actually test if it works
    run_sync("dotnet run -p Kari.Test")
    
    try_make_dir(NUGET_COPY_TO_PATH)

    # Move files to the needed folder
    for p in nuget_projects:
        copy_nuget_output(p)
        print(f'Copied {p} assembly NuGet output')

    # TODO: Maybe copy all assembiles? Will there be conflicts?
    if False:
        copy_all_nuget_output()
        

except subprocess.CalledProcessError as err:
    print(f'Build process exited with error code {err.returncode}')

