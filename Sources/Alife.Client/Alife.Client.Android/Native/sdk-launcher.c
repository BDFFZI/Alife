#include <dlfcn.h>
#include <stdio.h>
#include <stdlib.h>

typedef int (*host_main)(int, const char **, const char *, const char *, const char *);

int main(int argc, char **argv)
{
    const char *root = getenv("DOTNET_ROOT");
    const char *fxr = getenv("ALIFE_HOSTFXR");
    if (!root || !fxr) return 2;
    void *library = dlopen(fxr, RTLD_NOW | RTLD_LOCAL);
    if (!library) { fprintf(stderr, "%s\n", dlerror()); return 3; }
    host_main run = (host_main)dlsym(library, "hostfxr_main_startupinfo");
    if (!run) return 4;
    char host[4096];
    snprintf(host, sizeof(host), "%s/dotnet", root);
    argv[0] = host;
    // Explicit startup paths avoid identifying the system linker as the dotnet host.
    return run(argc, (const char **)argv, host, root, "");
}
