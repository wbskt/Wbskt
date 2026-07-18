wbskt platform ui

when we do the wbskt.com
we see the landing page for marketing, about, documentation, pricing, solutions, features etc
there will also be a login/signup button

this will be a usual login/signup page

once logged in, we go to console.wbskt.com

here the user will be taken to a screen listing workspaces. option to create workspaces depricate workspaces.
also if the user is an admin, they will have another page to manage the users and permissions for workspaces. users will also have usergroups/teams.

the permisisons heirarchy is as follows <insert access control flow>

the overall ui feel for the console.wbskt.com is of an ide. closely resempling the jetbrains rider's island theme.
take inspiration from jetbrains rider and postman.

now the components/entities within a workspace is
- clients
- policies
- workflows
- audit logs
- integrations

1. clients: these are connections to realtime/online/offline (socket connection to the socket server in the backend) the wbskt company provides client libraries in most of the languages and platform. people can use this linraries to integrate into embedded devies, iot devices, applications, scripts. websites.. ect.
   once a client is registered, it will show up in the client's explorer.

   when a client's page is opened, it will show, a text editor to send json payload, another section where it show's the recent/live in/out messages/comm, the uptime or the last online at time, the ping., capablities/properties/state variables of the client.

2. policies: client-policies, creating a policy will ask for a name and the mode (auto/onapprovel enroll , limited/unlimited num) it will create a unique pin for the policy and this pinn will be used by the client to register to the wbskt platform under a policy.
   so when a policy is selected, we should show the pin(masked, visible if cliked), remaining number of clients that can be registered if its mode is limited num. and showws the list of deivces that are registered using this policy. approvel pending deivces/ regiected devices/ and logs of when it was policy created/ device request/ device approvel/ device regect/ device revive/ policy desabled/ limit exeeded

3. workflow: this is the main heighlight. this is a workflow node based editor. we have 3 set's of nodes . trigger/ control/ action. another page will show the executions history/queue of the workflows. state/ properties. logs.

4. audit logs. self explanatory

5. integrations: enables user to add creds like telegram keys, twillo keys, other api keys, google account integration, discord, whatsapp, alexa .

now again regarding the ui within a workspace,
on the header- there will be a breadcrumbs of the path of the entity that i selected. an account icon showing the user image. the breadcrumb will start from the name of the workspace. this workspace can be switchable.

on the left side, there is a left side bar that has the icons for the client/policy/workflow/auditlogs/integrations explorr.

next to the left side bar we have the explorer its just like the explorer in jetbrains or vscode.

maincontent consists of a page switcher top bar right below the header like rider/postman/vscode. the page element within the page bar is clossable and scrollable
and below the page bar we ahve page content. this is rendered accordig to the page/entnty that is selected.

