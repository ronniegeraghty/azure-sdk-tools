# Azure SDK Assets Relocation -- "move recordings out of repos"

- [Azure SDK Assets Relocation -- "move recordings out of repos"](#azure-sdk-assets-relocation----move-recordings-out-of-repos)
  - [Setting Context](#setting-context)
  - [How the test-proxy can ease transition of external recordings](#how-the-test-proxy-can-ease-transition-of-external-recordings)
    - [Old](#old)
    - [New](#new)
  - [Evaluated  options for storage of these external recordings](#evaluated--options-for-storage-of-these-external-recordings)
    - [`Git SubModules`](#git-submodules)
      - [Advantages of `Git SubModules`](#advantages-of-git-submodules)
      - [Disadvantages of `Git SubModules`](#disadvantages-of-git-submodules)
    - [`Git lfs`](#git-lfs)
      - [Advantages of `Git lfs`](#advantages-of-git-lfs)
      - [Disadvantages of `Git lfs`](#disadvantages-of-git-lfs)
    - [`External Git Repo`](#external-git-repo)
      - [Advantages of `Git Repo`](#advantages-of-git-repo)
      - [Disadvantages of `Git Repo`](#disadvantages-of-git-repo)
    - [Blob Storage](#blob-storage)
      - [Advantages of `Blob Storage`](#advantages-of-blob-storage)
      - [Disadvantages of `Blob Storage`](#disadvantages-of-blob-storage)
    - [But if we already HAVE a problem with ever expanding git repos, why does an external repo help us?](#but-if-we-already-have-a-problem-with-ever-expanding-git-repos-why-does-an-external-repo-help-us)
  - [We have an external git repo, how will we integrate that with our frameworks?](#we-have-an-external-git-repo-how-will-we-integrate-that-with-our-frameworks)
    - [Auto-commits and merges to `main`](#auto-commits-and-merges-to-main)
    - [Drawbacks](#drawbacks)
  - [Scenario Walkthroughs](#scenario-walkthroughs)
    - [Single Dev: Update single service's recordings](#single-dev-update-single-services-recordings)
    - [Single Dev: Create a new services's recordings](#single-dev-create-a-new-servicess-recordings)
    - [Single Dev: Update recordings for a hotfix release](#single-dev-update-recordings-for-a-hotfix-release)
    - [Multiple Devs: Update the same pull request](#multiple-devs-update-the-same-pull-request)
    - [Multiple Devs: Update recordings for two packages under same service in parallel](#multiple-devs-update-recordings-for-two-packages-under-same-service-in-parallel)
  - [Asset sync script implementation](#asset-sync-script-implementation)
    - [Cross-platform capabilities](#cross-platform-capabilities)
    - [Interaction](#interaction)
    - [Implementation of Sync Script](#implementation-of-sync-script)
    - [Sync operations description and listing](#sync-operations-description-and-listing)
    - [Sync Operation triggers](#sync-operation-triggers)
    - [Sync Operation details - pull](#sync-operation-details---pull)
    - [Sync Operation details - push](#sync-operation-details---push)
    - [Integrating the sync script w/ language frameworks](#integrating-the-sync-script-w-language-frameworks)
  - [Test Run](#test-run)
  - [Integration Checklist](#integration-checklist)
  - [Post-Asset-Move space optimizations](#post-asset-move-space-optimizations)
    - [Test-Proxy creates seeded body content at playback time](#test-proxy-creates-seeded-body-content-at-playback-time)

## Setting Context

The Azure SDK team has a problem that has been been growing in the background for the past few years. Our repos are getting big! The biggest contributor to this issue are **recordings**. Yes, they compress well, but when bugfixes can result in entire rewrites of multiple recordings files, the compression ratio becomes immaterial.

We need to get these recordings _out_ of the main repos without impacting the day-to-day dev experience significantly.

```text
sdk-for-python/                            sdk-for-python-assets/         
  sdk/                                       sdk/
    tables/                                    tables/ 
      azure-data-tables/                         azure-data-tables/   
        tests/                                     tests/
          |-<recordings>--------|                    |--<moved recordings>-----|
          |   <delete>          |    relocate        |   recording_1           |
          |   <delete>          |      -->           |   recording_2           |
          |   <delete>          |                    |   recording_N           |
          |---------------------|                    |-------------------------|
    
```

The unfortunate fact of the matter is that an update like this _will_ impede users. The only thing we can do is mitigate the worst of these impacts.

Thankfully, the integration of the test-proxy actually provides a great opportunity for upheavel in the test areas! Not only would we be making big changes in the test area already, but the `storage context` feature of the test-proxy really lends itself well to this effort as well!

## How the test-proxy can ease transition of external recordings

With language-specific record/playback solutions, there must be an abstraction layer that retrieves the recording files from an expected `recordings` folder. Where these default locations are is usually predicated on the tech being used. It's not super terrible, but custom save/load would need to be implemented for each recording stack, with varying degrees of complexity depending on how opinionated a given framework is.

Contrast this with the the test-proxy, which starts with a **storage context**. This context is then used when **saving** and **loading** a recording file.

Users of the test proxy are required to provide a `relative path to test file from the root of the repo` as their recording file name. A great example of this would be...

```text
sdk/tables/azure-data-tables/recordings/test_retry.pyTestStorageRetrytest_no_retry.json
[----------------test path--------------------------][--------------test name----------]
```

[What this looks like in source](https://github.com/YalinLi0312/azure-sdk-for-python/blob/main/sdk/tables/azure-data-tables/tests/recordings/test_retry.pyTestStorageRetrytest_no_retry.json)

Given that the test-proxy is storing/retrieving data independent of the client code (other than the key), client side changes for a wholesale shift of the recordings location is actually simple. The source code for the test _won't need to change at all_. From the perspective of the client test code, nothing has functionally changed.

All that needs to happen is to:

1. Start the test proxy with storage context set to cloned assets repo (details forthcoming)
2. Adjust the provided "file path" to the recording within the asset repo.

If you were invoking the test-proxy as a docker image, the difference in initilization is as easy as:

### Old

`docker run -v C:/repo/sdk-for-python/:/etc/testproxy azsdkengsys.azurecr.io/engsys/testproxy-lin:latest`

### New

`docker run -v C:/repo/sdk-for-python-assets/:/etc/testproxy azsdkengsys.azurecr.io/engsys/testproxy-lin:latest`

Given the same relative path in the assets repo, 0 changes to test code are necessary.

## Evaluated  options for storage of these external recordings

Prior to ScottB starting on this project, JimS was the one leading the charge. As part of that work, Jim explored few potentional storage solutions. He did not evaluate these strictly from a `usability` standpoint.

- **external git repo**
- git modules
- git lfs
- blob **storage**

He also checked other measures, like `download speed` and `integration requirements`. Blob storage especially has a good story for "public read, private write", and _does_ support snapshotting. However, the cost is low bulk download speed when NOT being run on a tool like `azcopy`.

[These and other observations are recorded in his original document available here.](https://microsoft.sharepoint.com/:w:/t/AzureDeveloperExperience/EZ8CA-UTsENIoORsOxekfG8BzwoNV4xhVOIzTGmdk8j4rA?e=DFkiII)

### `Git SubModules`

#### Advantages of `Git SubModules`

- Publically browsable in a coherent fashion through the github UI

#### Disadvantages of `Git SubModules`

- Extremely unwieldy. The method for updating them locally is quite manual, and there is as far as I can tell no way to directly tie a submodule's commits to the main repos commits. They're still totally separate repositories. [As reviewable in their docs](https://git-scm.com/book/en/v2/Git-Tools-Submodules), git submodules aren't quite intended for large collaboration or projects. Especially not with multiple moving parts under the submodule.

### `Git lfs`

#### Advantages of `Git lfs`

- We remain in the same repository. `git lfs` just defers the download of specific sets of files.

#### Disadvantages of `Git lfs`

- Our repos stay massive, which defeats the purpose of this project.
  
### `External Git Repo`

#### Advantages of `Git Repo`

- Publically browsable in a coherent fashion through the github UI
- The level of customization necessary is purely on the language shims for automatic playback purposes

#### Disadvantages of `Git Repo`

- Need to deal with the fact that it's a git repo, and not a direct storage solution. We will need PRs, branches, and cleanup tasks running on the assets repo instead of a storage solution with no real sense of commit history.

### Blob Storage

#### Advantages of `Blob Storage`

- Very extensive versioning capabilities due to manual nature
- Accessible through basic REST API

#### Disadvantages of `Blob Storage`

- Extensive local scripting necessary to handle upload/download.
  - We would need to locally zip and upload. Individual file download is _far_ too enefficient, but a single snapshotted blob could definitely work.
  - Virtuall the same conflicts for download and unzip
- No publically available UI to browse recordings at rest

### But if we already HAVE a problem with ever expanding git repos, why does an external repo help us?

Because the automation interacting with this repository should only ever clone down _a single commit_ at one time.

Yes, commit histories do add _some_ weight to the git database, but it's definitely not a super impactful difference.

## We have an external git repo, how will we integrate that with our frameworks?

This is where the story gets complicated. Now that recordings are no longer stored directly alongside the code that they support, the process to _initially retrieve_ and _update_ the recordings gets a bit more stilted.

In a previous section, we established that another git repo is the most obvious solution.

As part of this project, we have established these `assets` repos for the four main languages.

- [Azure/azure-sdk-for-python-assets](https://github.com/Azure/azure-sdk-for-python-assets)
- [Azure/azure-sdk-for-js-assets](https://github.com/Azure/azure-sdk-for-js-assets)
- [Azure/azure-sdk-for-net-assets](https://github.com/Azure/azure-sdk-for-net-assets)
- [Azure/azure-sdk-for-java-assets](https://github.com/Azure/azure-sdk-for-java-assets)

So for a given language, we will have the following resources at play:

```bash
Azure/azure-sdk-for-<language>
Azure/azure-sdk-for-<language>-assets
```

Given the split nature of the above, We need to talk about how the test-proxy knows **which** recordings to grab. We can't simply default to `latest main`, as that will _not_ work if we need to run tests from an earlier released version of the SDK.

To get around this, we will embed a reference to an assets repo SHA into the language repository. As part of the test playback, local implementations must _retrieve_ these referenced assets at runtime. After retrieving, there will also need to be a process to sync any updated recordings _back_ to the recordings repo without an extremely large amount of manual effort.

As of now, it seems the best place to locate this assets SHA is in a new file in each `sdk/<service>` directory. For most of our packages this is a safe bet. Only one team member will be updating this SHA at a time, and as such it will be easy to add onto a commit one at a time. There is no parallelization! For others, like `azure-communication` in python or `spring` in Java land, this will be complex. We will revisit these topics in the [Scenarios Walkthroughs](#scenario-walkthroughs) section.


```bash
<repo-root>
  /sdk
    /<service>
      recordings.json
```

And within the file...

```json
{
    /*
      By default, the prefix path to a test file in the assets repo will be identical to the code repo.
        sdk/<service>/<package>/<recordings>/recording1.json
        [   prefix  ]
      

      EG: if "path-in-assets" is set to "tables", the prefix will no longer be present, which will result:
        tables/<package>/<recordings>/recording1.json

      Please note that making use of this prefix methodology is safe, as long a s
    */
    "prefix-path-in-assets": "recordings/", 

    // by default, will check out "main"
    "fallback-branch": "",

    // by default, will be resolved auto/<service>
    "auto-commit-branch": "auto/tables",

    // this json file will eventually need additional metadata, the below key just illustrates that
    "metadatakey1": "metadatavalue1",

    // no default for this value.
    "SHA": "4e8e976b7839c1e9c6903f48106e48be76868a5d"
}
```

While this works really well for local playback, it does not work for submitting a PR with your code changes. Why? Because the PR checks won't _have_ your updated assets repo that you may have created by recording your tests locally!

This necessitates a script that can be queued **against a local branch or PR** that will push a commit to the `assets` repo and then update the **local reference** within a `recordings.json` to consume it.

You will note that the above JSON configuration lends itself well to more individual solutions, while allowing space for more _targeted_ overrides later.

### Auto-commits and merges to `main`

We need to add nightly automation that squashes down these auto-commits into the `main` branch on a nightly cadence.

Let's walk through a scenario to show what this would look like.

When PRs are submitted, the SHAs referencing the assets repo will be _different_. This is due to the fact that each user will have pushed their new recordings to the assets repo separately form each other. They have no comprehension of "update from main". All individual SHAs remember!

```text
+---------------------------------------------------+---------------------------------------------------------------+
| azure-sdk-for-<language>/                         | assets-repo/                                                  |
|                                                   |                                                               |
|                                                   |                                                               |
|   sdk/core/record+--------------------------------+>auto-commit/core@SHA1                                         |
|      ...         |                                |   /recordings/sdk/core/azure-core/recordings/YYY.json         |
|      SHA: "SHA1" |                                |                                                               |
|      ...                               +----------+>auto-commit/storage@SHA2                                      |
|                                        |          |   /recordings/sdk/core/azure-storage-blob/recordings/XXX.json |
|   sdk/storage/recordings.json          |          |                                                               |
|      ...         +---------------------+          | hotfix-commit/storage@SHA3                                    |
|      SHA: "SHA2" |                                | ^ /recordings/sdk/core/azure-storage-blob/recordings/YYY.json |
|      ...                                          | |                                                             |
|                                                   | |                                                             |
|   sdk/storage/recordings.json (from release tag)  | |                                                             |
|      ...                                          | |                                                             |
|      SHA: "SHA3" ---------------------------------+-+                                                             |
|      ...                                          |                                                               |
|                                                   |                                                               |
+---------------------------------------------------+---------------------------------------------------------------+
```

Before nightly maintenenance, we will have merged the following to `main` in `azure-sdk-for-<language>`.

After nightly automation has copied commits into `main`, we will update the current recording.json files in `main` to reflect the newly merged _common_ commit.

- auto-commit/core@SHA1 -> commits trasnfered and squashed to main
- auto-commit/core@SHA2 -> commits trasnfered and squashed to main
- hotfix-commit/storage@SHA3 -> Stays around forever, like our hotfix branches do.

```text
+---------------------------------------------------+---------------------------------------------------------------+
| azure-sdk-for-<language>/                         | assets-repo/                                                  |
|                                                   |                                                               |
|                                                   |                                                               |
|   sdk/core/record      +---------------------------+>auto-commit/core@NewMainSHA                                  |
|      ...               |                          |   /recordings/sdk/core/azure-core/recordings/YYY.json         |
|      SHA: "NewMainSHA" |                          |                                                               |
|      ...                               +----------+>auto-commit/storage@NewMainSHA                                |
|                                        |          |   /recordings/sdk/core/azure-storage-blob/recordings/XXX.json |
|   sdk/storage/recordings.json          |          |                                                               |
|      ...               +---------------+          | hotfix-commit/storage@SHA3                                    |
|      SHA: "NewMainSHA" |                          | ^ /recordings/sdk/core/azure-storage-blob/recordings/YYY.json |
|      ...                                          | |                                                             |
|                                                   | |                                                             |
|   sdk/storage/recordings.json (from release tag)  | |                                                             |
|      ...                                          | |                                                             |
|      SHA: "SHA3" ---------------------------------+-+                                                             |
|      ...                                          |                                                               |
|                                                   |                                                               |
+---------------------------------------------------+---------------------------------------------------------------+
```

In this way, a concept of `main` can still exist, and will be used where possible, but daily progress will not be held up.

### Drawbacks

The `auto` commit branches will need to stick around. At least as far as we need to keep the commits in them that are referenced from the `azure-sdk-for-python` repo. Any commits present in the `azure-sdk-for-<language>-assets` and referenced anywhere from the `azure-sdk-for-<language>` repo MUST continue to exist. They cannot be automatically trimmed. For example, there is nothing stopping a dev from releasing a package when the assets repo SHA is set to the intial "auto" commit. We'd prefer that folks wait for the merge to `main` to release the package, but to enforce that will cause a whole acre of headache.

## Scenario Walkthroughs

### Single Dev: Update single service's recordings

### Single Dev: Create a new services's recordings

### Single Dev: Update recordings for a hotfix release

### Multiple Devs: Update the same pull request

### Multiple Devs: Update recordings for two packages under same service in parallel

## Asset sync script implementation

Alright, so we know how we want to structure the `recordings.json`, and we know WHAT needs to happen. Now we need to delve into the HOW this needs to happen. Colloqially, anything referred to as a `sync` operation should be understood to be part of these abstraction scripts handling git pull and push operations.

### Cross-platform capabilities

The example implementations of the sync script will be written in `pwsh`. While that will work for our regular CI and local testing, not all the azure-sdk devs 

### Interaction

```
main: (commitsha1) -> (commitsha2)

auto/tables: (commisha1) -> (commitsha2)
```

1. Sync for `playback`
   1. If there is no `recordings.json`, create it.
   2. If there is an existing `recordings.json`
      1. Check out assets repo sdk/service directory with the targeted SHA from `recordings.json`.
   3. Invoke Tests
   4. Stash changes. Pull
   5. Create new commit to branch `auto/<servicename>`. then push.

### Implementation of Sync Script

To remain as out of the way as possible, it would be rational to support two versions of these `sync` scripts. `pwsh` and `sh`. However, it is easy to see a world where we have bugs in one version or the other of the sync library.

Given that, we should probably just target `powershell`.

One of the azsdk repos may wish to integrate these `sync` scripts themselves rather than taking a direct dependency on powershell. This is totally fine, but the implementation will have to be maintained by that team. The EngSys team is signing up to deliver the generic abstraction, and will code to ensure that other abstractions are allowed.

### Sync operations description and listing

The external repo will just be a _git_ repo, so it's not like devs won't be able to `cd` into it and resolve any conflicts themselves. However, that should not be the _norm_. To avoid this, we need the following functionality into these scripts.

| Operation | Description |
|---|---|
| Sync | When one first checks out the repo, one must initialize the recordings repo so that we can run in `playback` mode.  |
| Push | Submits a PR to the assets repo, updates local `recordings.json` with new SHA. |
| Reset | Return assets repo and `recordings.json` to "just-cloned" state. |

### Sync Operation triggers

At the outset, this will need to be manually run. How far down this manual path do we want to go?

Options:

- `Pre-commit` hook ala typescript linting
  - This works for _changes_, but how about for a fresh repo? Initialize has gotta happen at some point. Automagic stuff could also result in erraneous PRs to the  
- Scripted invocation as part of a test run. For `ts/js`, this is actually simple, as `npm <x>` are just commands defined in the packages.json file. For others, this may be a bit closer to manual process.

### Sync Operation details - pull

The initialization of the assets repo locally should be a simple clone w/ 0 blobs.

```
.git
<files at root>
folder1/
folder2/
  folder3/
```

As `playback` sessions are started, the repo should:

1. Discard pending changes, reset to empty
2. Add `sparse-checkout` for the service folder needed by the current playback request.
3. `checkout` exact SHA specified in `recordings.json`
4. `pull`

Given the context advantages discussed earlier, one most only start the proxy at the root of the `assets` directory. Everything else should shake out from there.

### Sync Operation details - push

The `start` point here will be defined by what we settle on for the "main" branch.

### Integrating the sync script w/ language frameworks

Each repo has its own language-specific method to start the test-proxy. The _same method_ that starts that test-proxy needs to resolve these commit SHAs and leverage the assets script to checkout the local assets copy to the appropriate target version.

## Test Run

`scbedd` has created a [a test branch](https://github.com/scbedd/azure-sdk-for-python/tree/feature/move-recordings) that has a hacked up local version of everything we talk about above. The scripts are no where near complete and are merely proxies to ensure everything still works as we expect.

That custom script is present in `eng/common/testproxy/assets.ps1`.

Invoke it like:

- `assets.ps1 reset <directory>`
- `assets.ps1 playback <directory>`

So to locally repro this experience:

1. git checkout the branch linked to above.
2. `.\eng\common\TestResources\New-TestResources.ps1 'tables'` -> set environment variables
3. `assets.ps1 playback sdk/tables`
4. `cd sdk/tables/azure-data-tables/`
5. `pip install .`
6. `pip install -r dev_requirements.txt`
7. `pytest`

## Integration Checklist

What needs to be done on each of the repos to utilize this.

todo this list

- [ ] 
- [ ]
- [ ]
- [ ]
- [ ]
- [ ]
- [ ]
- [ ]

FEEDBACK FROM WES
  MERGE COMMITS ONLY WORK IF A SINGLE COMMIT IS NOT SQUASHED.
    Merge commits do not work.
  
  Description of base methodology ->
    conflicts shouldn't happen -> just re-record -> push to branch
    Map out what this would look like. They should be based off master.

  ensure that we leave a comment describing why
  Split the `recordings.json` into a service directory

## Post-Asset-Move space optimizations

### Test-Proxy creates seeded body content at playback time

For more than a few of the storage recordings, some request bodies are obviously made up of generated content. We can significantly reduce the amount of data that is actually needed to be stored by allowing the test-proxy to _fill in_ these request bodies based on a known seed value (maybe from the original request body?).