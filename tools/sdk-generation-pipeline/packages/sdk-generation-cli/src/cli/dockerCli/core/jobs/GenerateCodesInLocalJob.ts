import { existsSync } from 'fs';
import * as path from 'path';

import { cloneRepo, getChangedPackageDirectory } from '../../../../utils/git';
import { sdkToRepoMap } from '../constants';
import { DockerContext } from '../DockerContext';
import { DockerTaskEngineContext } from '../DockerTaskEngineContext';
import { BaseJob } from './BaseJob';

export class GenerateCodesInLocalJob extends BaseJob {
    context: DockerContext;

    constructor(context: DockerContext) {
        super();
        this.context = context;
    }

    public async cloneRepoIfNotExist(sdkRepos: string[]) {
        for (const sdkRepo of sdkRepos) {
            if (!existsSync(path.join(this.context.workDir, sdkRepo))) {
                await cloneRepo(sdkRepo, this.context.workDir, this.context.logger);
            }
            this.context.sdkRepo = path.join(this.context.workDir, sdkRepo);
        }
    }

    public async execute() {
        const sdkRepos: string[] = this.context.sdkList.map((ele) => sdkToRepoMap[ele]);
        await this.cloneRepoIfNotExist(sdkRepos);
        for (const sdk of this.context.sdkList) {
            this.context.sdkRepo = path.join(this.context.workDir, sdkToRepoMap[sdk]);
            const dockerTaskEngineContext = new DockerTaskEngineContext();
            dockerTaskEngineContext.initialize(this.context);
            await dockerTaskEngineContext.runTaskEngine();
        }

        const generatedCodesPath: Map<string, Set<string>> = new Map();

        for (const sdk of this.context.sdkList) {
            generatedCodesPath[sdk] = await getChangedPackageDirectory(path.join(this.context.workDir, sdkToRepoMap[sdk]));
        }

        this.context.logger.info(`Finish generating sdk for ${this.context.sdkList.join(', ')}.`);
        for (const sdk of this.context.sdkList) {
            if (generatedCodesPath[sdk].size > 0) {
                this.context.logger.info(`You can find changed files of ${sdk} in:`);
                generatedCodesPath[sdk].forEach((ele) => {
                    this.context.logger.info(`    - ${path.join(this.context.workDir, sdkToRepoMap[sdk], ele)}`);
                });
            } else {
                this.context.logger.info(`Cannot find changed files of ${sdk} because there is no git diff.`);
            }
        }
        this.context.logger.info(`You can use vscode to connect this docker container for further development.`);
        this.doNotExitDockerContainer();
    }
}
