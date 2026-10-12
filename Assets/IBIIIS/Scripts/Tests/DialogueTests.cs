using System.Collections.Generic;
using IBIIIS.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace IBIIIS.Tests
{
    public sealed class DialogueTests
    {
        private readonly List<Object> made = new List<Object>();
        private DialogueCharacter hero, fly;
        private Sprite heroFace, heroAngry, flyFace;
        [SetUp] public void Setup()
        {
            heroFace = Track(Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero));
            heroAngry = Track(Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero));
            flyFace = Track(Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.zero));
            hero = Track(ScriptableObject.CreateInstance<DialogueCharacter>());
            hero.Set("hero", "주인공", DialogueSide.Left, heroFace, Color.cyan, new[] { new DialogueExpression { id = "angry", portrait = heroAngry } });
            fly = Track(ScriptableObject.CreateInstance<DialogueCharacter>());
            fly.Set("fly", "어시 파리", DialogueSide.Right, flyFace, Color.green);
        }
        [TearDown] public void Cleanup()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o is Component c ? c.gameObject : o);
            made.Clear();
        }
        private T Track<T>(T o) where T : Object { made.Add(o); return o; }
        private DialogueLine[] Script() => new[]
        {
            DialogueLine.Narrate("주인공이 접속한다."),
            DialogueLine.Say(fly, "안녕!"),
            DialogueLine.Say(hero, "...파리?", "angry"),
        };

        [Test] public void TextTypesOutAndAdvanceFinishesThenMovesOn()
        {
            var runner = new DialogueRunner { CharactersPerSecond = 10 }; int finished = 0;
            runner.Start(Script(), () => finished++);
            Assert.IsTrue(runner.IsNarration); Assert.AreEqual("", runner.ShownText, "처음엔 글자가 없음");
            runner.Tick(.3f); Assert.AreEqual("주인공", runner.ShownText, "초당 10자");
            runner.Advance(); Assert.AreEqual(0, runner.Index); Assert.IsTrue(runner.LineComplete, "나오는 중이면 전체를 바로 보임");
            runner.Advance(); Assert.AreEqual(1, runner.Index, "다 나왔으면 다음 줄");
            runner.Advance(); runner.Advance(); runner.Advance(); runner.Advance();
            Assert.IsFalse(runner.IsOpen, "마지막 줄 다음에 끝남"); Assert.AreEqual(1, finished, "끝 알림 한 번");
            runner.Advance(); Assert.AreEqual(1, finished);
        }
        [Test] public void SpeakersStandOnTheirSideAndTheSpeakerIsHighlighted()
        {
            var runner = new DialogueRunner { CharactersPerSecond = 0 };
            runner.Start(Script());
            Assert.IsNull(runner.Left); Assert.IsNull(runner.Right); Assert.IsNull(runner.ActiveSide, "지문은 말하는 쪽 없음");
            runner.Advance();
            Assert.AreEqual(fly, runner.Right); Assert.AreEqual(DialogueSide.Right, runner.ActiveSide); Assert.AreEqual("어시 파리", runner.SpeakerName);
            runner.Advance();
            Assert.AreEqual(hero, runner.Left); Assert.AreEqual(fly, runner.Right, "앞서 말한 인물은 자기 쪽에 남음");
            Assert.AreEqual(DialogueSide.Left, runner.ActiveSide); Assert.AreEqual("angry", runner.LeftExpression);
            Assert.AreEqual(heroAngry, hero.Portrait("angry")); Assert.AreEqual(heroFace, hero.Portrait("없는표정"), "없는 표정은 기본 초상화");
        }
        [Test] public void EmptyDialogueFinishesImmediately()
        {
            int finished = 0; var runner = new DialogueRunner();
            runner.Start(new DialogueLine[0], () => finished++);
            Assert.IsFalse(runner.IsOpen); Assert.AreEqual(1, finished);
        }
        [Test] public void ViewDrawsNamePortraitsAndNarration()
        {
            var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(GameUiSetup.DialogueBoxPath).CloneTree();
            int clicks = 0; var view = new DialogueView(root, () => clicks++);
            var runner = new DialogueRunner { CharactersPerSecond = 0 };
            runner.Start(Script()); view.Show(runner, 0);
            Assert.AreEqual(DisplayStyle.None, root.Q<Label>("name").style.display.value, "지문은 이름표 없음");
            Assert.IsTrue(root.Q<Label>("text").ClassListContains("narration")); Assert.AreEqual("주인공이 접속한다.", root.Q<Label>("text").text);
            runner.Advance(); runner.Advance(); view.Show(runner, 0);
            Assert.AreEqual("주인공", root.Q<Label>("name").text);
            Assert.IsTrue(root.Q("portrait-left").ClassListContains("speaking")); Assert.IsTrue(root.Q("portrait-right").ClassListContains("listening"));
            Assert.AreEqual(heroAngry, root.Q("portrait-left").style.backgroundImage.value.sprite, "표정 초상화");
            runner.Close(); view.Show(runner, 0); Assert.AreEqual(DisplayStyle.None, root.style.display.value, "끝나면 숨김");
        }
        [Test] public void NpcWithDialogueOpensTheDialogueAndClosesAtTheEnd()
        {
            var script = Track(ScriptableObject.CreateInstance<DialogueScript>()); script.Set("test", Script());
            var npc = Track(new GameObject("NPC").AddComponent<NpcSpeaker>());
            var so = new SerializedObject(npc); so.FindProperty("dialogue").objectReferenceValue = script; so.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual("말 걸기", npc.PromptVerb);
            npc.Interact(null);
            Assert.IsTrue(npc.InDialogue); Assert.IsNull(npc.PromptVerb, "대화 중에는 다시 말 걸 수 없음");
            var controller = Object.FindAnyObjectByType<DialogueController>(); Track(controller);
            Assert.IsFalse(controller.UsesGameUi, "Game UI 없이 열면 임시 표시");
            for (int i = 0; i < 10 && controller.IsOpen; i++) controller.Runner.Advance();
            Assert.IsFalse(npc.InDialogue); Assert.AreEqual("말 걸기", npc.PromptVerb);
        }
    }
}
