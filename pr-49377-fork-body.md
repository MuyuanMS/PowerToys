> Mirrored from microsoft/PowerToys PR 49377 for review iteration

Bumps [actions/setup-node](https://github.com/actions/setup-node) from 6 to 7.
<details>
<summary>Release notes</summary>
<p><em>Sourced from <a href="https://github.com/actions/setup-node/releases">actions/setup-node's releases</a>.</em></p>
<blockquote>
<h2>v7.0.0</h2>
<h2>What's Changed</h2>
<h3>Enhancements:</h3>
<ul>
<li>Add cache-primary-key and cache-matched-key as outputs by <a href="https://github.com/gowridurgad"><code>@​gowridurgad</code></a> in <a href="https://redirect.github.com/actions/setup-node/pull/1577">actions/setup-nodePR 1577</a></li>
<li>Migrate to ESM and upgrade dependencies by <a href="https://github.com/gowridurgad"><code>@​gowridurgad</code></a> in <a href="https://redirect.github.com/actions/setup-node/pull/1574">actions/setup-nodePR 1574</a></li>
</ul>
<h3>Bug fixes:</h3>
<ul>
<li>Remove dummy NODE_AUTH_TOKEN export by <a href="https://github.com/gowridurgad"><code>@​gowridurgad</code></a> in <a href="https://redirect.github.com/actions/setup-node/pull/1558">actions/setup-nodePR 1558</a></li>
<li>Only use <code>mirrorToken</code> in <code>getManifest</code> if it's provided by <a href="https://github.com/deiga"><code>@​deiga</code></a> in <a href="https://redirect.github.com/actions/setup-node/pull/1548">actions/setup-nodePR 1548</a></li>
</ul>
<h3>Documentation updates:</h3>
<ul>
<li>Add documentation for publishing to npm with Trusted Publisher (OIDC) by <a href="https://github.com/chiranjib-swain"><code>@​chiranjib-swain</code></a> in <a href="https://redirect.github.com/actions/setup-node/pull/1536">actions/setup-nodePR 1536</a></li>
<li>docs: Update restore-only cache documentation by <a href="https://github.com/priya-kinthali"><code>@​priya-kinthali</code></a> in <a href="https://redirect.github.com/actions/setup-node/pull/1550">actions/setup-nodePR 1550</a></li>
<li>docs: Update caching recommendations to mitigate cache poisoning risks by <a href="https://github.com/chiranjib-swain"><code>@​chiranjib-swain</code></a> in <a href="https://redirect.github.com/actions/setup-node/pull/1567">actions/setup-nodePR 1567</a></li>
</ul>
<h3>Dependency update:</h3>
<ul>
<li>Upgrade <code>@​actions/cache</code> to 5.1.0, log cache write denied by <a href="https://github.com/jasongin"><code>@​jasongin</code></a> in <a href="https://redirect.github.com/actions/setup-node/pull/1569">actions/setup-nodePR 1569</a></li>
</ul>
<h2>New Contributors</h2>
<ul>
<li><a href="https://github.com/chiranjib-swain"><code>@​chiranjib-swain</code></a> made their first contribution in <a href="https://redirect.github.com/actions/setup-node/pull/1536">actions/setup-nodePR 1536</a></li>
<li><a href="https://github.com/deiga"><code>@​deiga</code></a> made their first contribution in <a href="https://redirect.github.com/actions/setup-node/pull/1548">actions/setup-nodePR 1548</a></li>
<li><a href="https://github.com/jasongin"><code>@​jasongin</code></a> made their first contribution in <a href="https://redirect.github.com/actions/setup-node/pull/1569">actions/setup-nodePR 1569</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/actions/setup-node/compare/v6...v7.0.0">https://github.com/actions/setup-node/compare/v6...v7.0.0</a></p>
<h2>v6.5.0</h2>
<h2>What's Changed</h2>
<ul>
<li>Update <code>@​actions/cache</code> to 5.1.0 and add security overrides for undici and fast-xml-parser by <a href="https://github.com/HarithaVattikuti"><code>@​HarithaVattikuti</code></a> in <a href="https://redirect.github.com/actions/setup-node/pull/1579">actions/setup-nodePR 1579</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/actions/setup-node/compare/v6.4.0...v6.5.0">https://github.com/actions/setup-node/compare/v6.4.0...v6.5.0</a></p>
<h2>v6.4.0</h2>
<h2>What's Changed</h2>
<h3>Dependency updates:</h3>
<ul>
<li>Upgrade <a href="https://github.com/actions"><code>@​actions</code></a> dependencies by <a href="https://github.com/Copilot"><code>@​Copilot</code></a> in <a href="https://redirect.github.com/actions/setup-node/pull/1525">actions/setup-nodePR 1525</a></li>
<li>Update Node.js versions in versions.yml and bump package to v6.4.0  by <a href="https://github.com/priya-kinthali"><code>@​priya-kinthali</code></a> in <a href="https://redirect.github.com/actions/setup-node/pull/1533">actions/setup-nodePR 1533</a></li>
</ul>
<h2>New Contributors</h2>
<ul>
<li><a href="https://github.com/Copilot"><code>@​Copilot</code></a> made their first contribution in <a href="https://redirect.github.com/actions/setup-node/pull/1525">actions/setup-nodePR 1525</a></li>
</ul>
<p><strong>Full Changelog</strong>: <a href="https://github.com/actions/setup-node/compare/v6...v6.4.0">https://github.com/actions/setup-node/compare/v6...v6.4.0</a></p>
<h2>v6.3.0</h2>
<h2>What's Changed</h2>
<h3>Enhancements:</h3>
<ul>
<li>Support parsing <code>devEngines</code> field by <a href="https://github.com/susnux"><code>@​susnux</code></a> in <a href="https://redirect.github.com/actions/setup-node/pull/1283">actions/setup-nodePR 1283</a></li>
</ul>
<!-- raw HTML omitted -->
</blockquote>
<p>... (truncated)</p>
</details>
<details>
<summary>Commits</summary>
<ul>
<li><a href="https://github.com/actions/setup-node/commit/820762786026740c76f36085b0efc47a31fe5020"><code>8207627</code></a> Migrate to ESM and upgrade dependencies (<a href="https://redirect.github.com/actions/setup-node/issues/1574">PR 1574</a>)</li>
<li><a href="https://github.com/actions/setup-node/commit/04be95cf3511ea51ebf9f224ddfb99cc7ab87cd4"><code>04be95c</code></a> Add cache-primary-key and cache-matched-key as outputs (<a href="https://redirect.github.com/actions/setup-node/issues/1577">PR 1577</a>)</li>
<li><a href="https://github.com/actions/setup-node/commit/7c2c68d20d402ed6a201ada70a81341941093140"><code>7c2c68d</code></a> docs: Update caching recommendations to mitigate cache poisoning risks (<a href="https://redirect.github.com/actions/setup-node/issues/1567">PR 1567</a>)</li>
<li><a href="https://github.com/actions/setup-node/commit/6a61c0375d66246de94630495909f12cf8dac84d"><code>6a61c03</code></a> Merge pull request <a href="https://redirect.github.com/actions/setup-node/issues/1569">PR 1569</a> from jasongin/update-actions-cache-5.1.0</li>
<li><a href="https://github.com/actions/setup-node/commit/30eb73b41ded577900c1ebf968ef95cdf8f7434f"><code>30eb73b</code></a> Resolve high-severity audit issues</li>
<li><a href="https://github.com/actions/setup-node/commit/4e1a87a501d0302f99e30e2748568adcb388d09f"><code>4e1a87a</code></a> Update dist</li>
<li><a href="https://github.com/actions/setup-node/commit/360237f0c01778d0c17291f75c56d6feae4f7574"><code>360237f</code></a> Strict equality</li>
<li><a href="https://github.com/actions/setup-node/commit/4f8aac5beb2f0854bc79651567a18c67eb0b9de3"><code>4f8aac5</code></a> Bump <code>@​actions/cache</code> to 5.1.0, log cache write denied</li>
<li><a href="https://github.com/actions/setup-node/commit/f4a67bbeca970f103397d3d2b9462cf787cd2980"><code>f4a67bb</code></a> Only use <code>mirrorToken</code> in <code>getManifest</code> if it's provided (<a href="https://redirect.github.com/actions/setup-node/issues/1548">PR 1548</a>)</li>
<li><a href="https://github.com/actions/setup-node/commit/0355742c943ddb13ca8a6b700f824231caa91e75"><code>0355742</code></a> Remove dummy NODE_AUTH_TOKEN export (<a href="https://redirect.github.com/actions/setup-node/issues/1558">PR 1558</a>)</li>
<li>Additional commits viewable in <a href="https://github.com/actions/setup-node/compare/v6...v7">compare view</a></li>
</ul>
</details>
<br />


[![Dependabot compatibility score](https://dependabot-badges.githubapp.com/badges/compatibility_score?dependency-name=actions/setup-node&package-manager=github_actions&previous-version=6&new-version=7)](https://docs.github.com/en/github/managing-security-vulnerabilities/about-dependabot-security-updates#about-compatibility-scores)

Dependabot will resolve any conflicts with this PR as long as you don't alter it yourself. You can also trigger a rebase manually by commenting `@dependabot rebase`.

[//]: # (dependabot-automerge-start)
[//]: # (dependabot-automerge-end)

---

<details>
<summary>Dependabot commands and options</summary>
<br />

You can trigger Dependabot actions by commenting on this PR:
- `@dependabot rebase` will rebase this PR
- `@dependabot recreate` will recreate this PR, overwriting any edits that have been made to it
- `@dependabot show <dependency name> ignore conditions` will show all of the ignore conditions of the specified dependency
- `@dependabot ignore this major version` will close this PR and stop Dependabot creating any more for this major version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this minor version` will close this PR and stop Dependabot creating any more for this minor version (unless you reopen the PR or upgrade to it yourself)
- `@dependabot ignore this dependency` will close this PR and stop Dependabot creating any more for this dependency (unless you reopen the PR or upgrade to it yourself)


</details>
