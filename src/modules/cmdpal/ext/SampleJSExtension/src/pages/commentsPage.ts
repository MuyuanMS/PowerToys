// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

import { ContentPageBase, ExtensionHost } from '@microsoft/cmdpal-sdk';
import type { CommandResult, Content, FormContent, TreeContent } from '@microsoft/cmdpal-sdk';
import { icon } from '../util.js';

const postTemplate = JSON.stringify({
  $schema: 'http://adaptivecards.io/schemas/adaptive-card.json',
  type: 'AdaptiveCard',
  version: '1.6',
  body: [{ type: 'TextBlock', text: '${postBody}', wrap: true }],
  actions: [
    {
      type: 'Action.ShowCard',
      title: '${replyCard.title}',
      card: {
        type: 'AdaptiveCard',
        $schema: 'http://adaptivecards.io/schemas/adaptive-card.json',
        version: '1.6',
        body: [
          {
            type: 'Container',
            id: '${replyCard.idPrefix}Properties',
            items: [
              {
                $data: '${replyCard.fields}',
                type: 'Input.Text',
                label: '${label}',
                id: '${id}',
                isRequired: '${required}',
                isMultiline: true,
                errorMessage: "'${label}' is required",
              },
            ],
          },
        ],
        actions: [{ type: 'Action.Submit', title: 'Post' }],
      },
    },
    { type: 'Action.Submit', title: 'Favorite' },
    { type: 'Action.Submit', title: 'View on web' },
  ],
});

/**
 * A single post in the comment tree. Mirrors the C# `PostContent`/`PostForm`.
 *
 * Each post owns a stable `formId` so the host can route a reply submission back
 * to this exact post even though the post's form lives deep in a lazily authored
 * tree. The ids are minted once per post instance, and the page keeps its post
 * instances alive (see `SampleCommentsPage`), so a reply added to a post is
 * retained on the model.
 *
 * The page supplies a callback that raises `contentPage/itemsChanged` after a
 * reply is appended. The host then re-fetches the tree so the new reply appears
 * without requiring the user to navigate away and back.
 */
class Post implements TreeContent {
  readonly type = 'tree';
  readonly replies: Post[] = [];
  readonly formId: string;

  constructor(
    private readonly body: string,
    private readonly raiseItemsChanged: () => void,
  ) {
    this.formId = `comment-form-${nextPostId()}`;
  }

  get rootContent(): Content {
    const dataJson = JSON.stringify({
      postBody: this.body,
      replyCard: {
        title: 'Reply',
        idPrefix: 'reply',
        fields: [
          { label: 'Reply', id: 'ReplyBody', required: true, placeholder: 'Write a reply here' },
        ],
      },
    });

    const form: FormContent = {
      type: 'form',
      formId: this.formId,
      templateJson: postTemplate,
      dataJson,
      submitForm: (inputs: string): CommandResult => {
        try {
          const parsed = JSON.parse(inputs) as { ReplyBody?: string };
          const reply = parsed.ReplyBody;
          if (reply) {
            this.replies.push(new Post(reply, this.raiseItemsChanged));
            this.raiseItemsChanged();
            ExtensionHost.showStatus('Reply posted', 'success');
          }
        } catch {
          // Ignore malformed form payloads.
        }
        return { kind: 'keepOpen' };
      },
    };
    return form;
  }

  getChildren(): Content[] {
    return [...this.replies];
  }
}

let postCounter = 0;
function nextPostId(): number {
  postCounter += 1;
  return postCounter;
}

function post(body: string, raiseItemsChanged: () => void, replies: string[] = []): Post {
  const p = new Post(body, raiseItemsChanged);
  for (const reply of replies) {
    p.replies.push(new Post(reply, raiseItemsChanged));
  }
  return p;
}

/** A page of nested comment threads. Mirrors the C# `SampleCommentsPage`. */
export class SampleCommentsPage extends ContentPageBase {
  readonly id = 'sample-comments-page';
  readonly name = 'View Posts';
  readonly title = 'View Posts';

  override icon = icon('\uE90A');

  private readonly posts: Post[];

  private readonly tree: TreeContent = {
    type: 'tree',
    rootContent: {
      type: 'markdown',
      body: [
        '# Example of a thread of comments',
        'You can use TreeContent in combination with FormContent to build a structure like a page with comments.',
        '',
        'The forms on this page use the AdaptiveCard `Action.ShowCard` action to show a nested, hidden card on the form.',
      ].join('\n'),
    },
    getChildren: (): Content[] => [...this.posts],
  };

  constructor() {
    super();

    const refresh = () => this.notifyItemsChanged();
    this.posts = [
      post('First', refresh, ["Oh very insightful. I hadn't considered that", 'Second', 'ah the ol switcheroo']),
      post('First\nEDIT: shoot', refresh, ['delete this']),
      post('Do you think they get the picture', refresh, ['Probably! Now go build and be happy']),
    ];
  }

  override getContent(): Content[] {
    return [this.tree];
  }
}
