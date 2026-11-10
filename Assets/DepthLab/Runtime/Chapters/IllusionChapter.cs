using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DepthLab
{
    public class IllusionChapter : LabChapter
    {
        public TMP_Text explanationLabel;
        public GameObject ponzoDisplay;
        public GameObject mullerDisplay;
        public Button[] ponzoAnswerButtons;
        public Button[] mullerAnswerButtons;
        public Button continueButton;

        private enum LessonStage
        {
            PonzoQuestion,
            PonzoExplanation,
            MullerQuestion,
            MullerExplanation,
            Finished
        }

        private LessonStage stage;
        private bool paused;
        private string explanation = "";

        public override string Objective => stage == LessonStage.PonzoQuestion ? "比较前方两个方块，在操作台选择「一样大」或「不一样」。"
            : stage == LessonStage.MullerQuestion ? "只比较中间的线段，选择「一样长」或「不一样」。"
            : stage == LessonStage.Finished ? "走进亮起的出口，前往光影实验室。"
            : "答对了！点击「继续」，进入下一步。";
        public override string Hint
        {
            get
            {
                switch (stage)
                {
                    case LessonStage.PonzoQuestion:
                        return "1. 比较立方体的实际大小并选择答案；别被背景汇聚线误导。";
                    case LessonStage.MullerQuestion:
                        return "2. 比较中间的直线段并选择答案；不计两端的箭翼。";
                    case LessonStage.PonzoExplanation:
                    case LessonStage.MullerExplanation:
                        return "先阅读本题解释，再点击“继续”进入下一步。";
                    default:
                        return "3. 两项比较已完成。请步行穿过已开启的出口。";
                }
            }
        }
        public override int Progress => stage == LessonStage.PonzoQuestion ? 0 :
            stage == LessonStage.PonzoExplanation || stage == LessonStage.MullerQuestion ? 1 : 2;
        public override int Goal => 2;
        protected override bool PuzzleSolved => stage == LessonStage.Finished;

        public override void Begin()
        {
            stage = LessonStage.PonzoQuestion;
            paused = false;
            explanation = "1. 两个立方体的实际大小相同，还是不同？请选择答案。";
            ResetExit();
            RefreshStatus();
            RefreshControls();
        }

        protected override void Update()
        {
            base.Update();
            RefreshControls();
        }

        public override void SetPaused(bool value)
        {
            paused = value;
            base.SetPaused(value);
            RefreshControls();
        }

        public void AnswerPonzoEqual() => Answer(LessonStage.PonzoQuestion, true);
        public void AnswerPonzoDifferent() => Answer(LessonStage.PonzoQuestion, false);
        public void AnswerMullerEqual() => Answer(LessonStage.MullerQuestion, true);
        public void AnswerMullerDifferent() => Answer(LessonStage.MullerQuestion, false);

        public void ContinueLesson()
        {
            if (!CanInteract || paused)
                return;

            if (stage == LessonStage.PonzoExplanation)
            {
                stage = LessonStage.MullerQuestion;
                explanation = "2. 两条直线段等长，还是不等长？忽略两端箭翼，选择答案。";
            }
            else if (stage == LessonStage.MullerExplanation)
            {
                stage = LessonStage.Finished;
                explanation = "3. 比较完成：背景影响大小与长度的感知，几何形状并未改变。请步行穿过已开启的出口。";
            }
            else
            {
                return;
            }

            RefreshStatus();
            RefreshControls();
        }

        private void Answer(LessonStage question, bool equal)
        {
            if (!CanInteract || paused || stage != question)
                return;

            bool ponzo = question == LessonStage.PonzoQuestion;
            if (!equal)
            {
                Mistake();
                explanation = ponzo
                    ? "1. 再试一次：背景汇聚线让同样大的立方体看似不同；只比较立方体本身。"
                    : "2. 再试一次：箭翼影响长度感知；只比较中间的直线段，不计箭翼。";
            }
            else
            {
                stage = ponzo ? LessonStage.PonzoExplanation : LessonStage.MullerExplanation;
                explanation = ponzo
                    ? "答对了：两个方块一样大！背景让它们看起来不同。\n点击「继续」比较线段。"
                    : "答对了：中间的线段一样长！两端箭头改变了你的感觉。\n点击「继续」开启出口。";
                DepthLabApp.Instance.ReportTutorialAction("answer");
            }

            RefreshStatus();
            RefreshControls();
        }

        private void RefreshControls()
        {
            bool ponzo = stage == LessonStage.PonzoQuestion || stage == LessonStage.PonzoExplanation;
            if (ponzoDisplay != null)
                ponzoDisplay.SetActive(ponzo);
            if (mullerDisplay != null)
                mullerDisplay.SetActive(!ponzo);
            if (explanationLabel != null)
                explanationLabel.text = explanation;

            bool canAnswer = CanInteract && !paused;
            SetAnswerButtons(ponzoAnswerButtons, stage == LessonStage.PonzoQuestion, canAnswer);
            SetAnswerButtons(mullerAnswerButtons, stage == LessonStage.MullerQuestion, canAnswer);
            if (continueButton != null)
            {
                bool showContinue = stage == LessonStage.PonzoExplanation || stage == LessonStage.MullerExplanation;
                continueButton.gameObject.SetActive(showContinue);
                continueButton.interactable = showContinue && canAnswer;
            }
        }

        private static void SetAnswerButtons(Button[] buttons, bool visible, bool canAnswer)
        {
            if (buttons == null)
                return;
            foreach (Button button in buttons)
            {
                if (button == null)
                    continue;
                button.gameObject.SetActive(visible);
                button.interactable = visible && canAnswer;
            }
        }
    }
}
