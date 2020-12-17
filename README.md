# Project 4 Part2 - TwitterClone using Websockets

## Team members
Kanika Sharma | UFID : 7119-1343 <br />
Divyatha Satheeshan Edachana | UFID : 0710-8354

## Instructions to run the program :
1. Unzip SharmaEdachana.zip						
2. Go to directory TwitterclonePart2 by using the command :
cd TwitterclonePart2
3. Ensure AppClient.fsx, AppServer.fsx and Messages.fsx are included in the compile in .fsproj file						
4. Through the command line, run the program using the commands :		 <br />						
First run the server process using :  <br />
dotnet fsi AppServer.fsx  <br />
After server has started running, run the client processes in different terminals (based on number of users to be simulated) :  <br />
dotnet fsi AppClient.fsx <br />
Once the client process is started, the user will be asked to give an Input Command. Press any key and it will list all the functionalities available in the system.
From the list, user can pick any functionality as per the format.
Eg : Register|1|abc
where username is 1 and password is abc
Note : Please give integer values for username.

## Youtube link for the demo :
https://youtu.be/BS6c-i64zyk

## Working Part :

<ul>
<li>The project runs as per the requirements.</li>
<li>We designed a JSON based API which represents all messages and their replies.</li>
<li>We have modified parts of our previous part1 engine using Suave to implement the WebSocket interface.</li>  
<li>We have modified parts of our previous part1 client to use WebSockets.</li>
</ul>

## Implementation :

<ul>
<li>We have used Suave which is a F# web development library for creating and using Websockets.</li>
<li>We have used JSON based APIs using simple REST principles to communicate between client and server</li>
</ul>
