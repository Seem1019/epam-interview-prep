using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EpamPrep.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InterviewSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    Topics = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentTopicIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentDepth = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAction = table.Column<string>(type: "TEXT", nullable: true),
                    PlannedMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TopicStartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    OpeningDirective = table.Column<string>(type: "TEXT", nullable: false),
                    ReportJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Lessons",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TopicKey = table.Column<string>(type: "TEXT", nullable: false),
                    SubtopicKey = table.Column<string>(type: "TEXT", nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lessons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanTasks",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Done = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanTasks", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    CvText = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PronunciationTerms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Term = table.Column<string>(type: "TEXT", nullable: false),
                    Respelling = table.Column<string>(type: "TEXT", nullable: false),
                    Ipa = table.Column<string>(type: "TEXT", nullable: true),
                    TipEs = table.Column<string>(type: "TEXT", nullable: true),
                    Accepted = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    Successes = table.Column<int>(type: "INTEGER", nullable: false),
                    LastHeard = table.Column<string>(type: "TEXT", nullable: true),
                    LastPracticedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PronunciationTerms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QuizQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TopicKey = table.Column<string>(type: "TEXT", nullable: false),
                    Subtopic = table.Column<string>(type: "TEXT", nullable: true),
                    Level = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: true),
                    CodeLang = table.Column<string>(type: "TEXT", nullable: true),
                    OptionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    CorrectOption = table.Column<int>(type: "INTEGER", nullable: true),
                    ExpectedOutput = table.Column<string>(type: "TEXT", nullable: true),
                    ModelAnswer = table.Column<string>(type: "TEXT", nullable: true),
                    ExplanationEs = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizQuestions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InterviewTurns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    TopicKey = table.Column<string>(type: "TEXT", nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    Question = table.Column<string>(type: "TEXT", nullable: false),
                    Answer = table.Column<string>(type: "TEXT", nullable: true),
                    AnswerSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    InputMode = table.Column<string>(type: "TEXT", nullable: true),
                    WordCount = table.Column<int>(type: "INTEGER", nullable: true),
                    DirectiveAfterAnswer = table.Column<string>(type: "TEXT", nullable: true),
                    AssessmentJson = table.Column<string>(type: "TEXT", nullable: true),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    Level = table.Column<string>(type: "TEXT", nullable: true),
                    AskedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterviewTurns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InterviewTurns_InterviewSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "InterviewSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuizAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QuestionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TopicKey = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", nullable: false),
                    Answer = table.Column<string>(type: "TEXT", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Passed = table.Column<bool>(type: "INTEGER", nullable: false),
                    FeedbackJson = table.Column<string>(type: "TEXT", nullable: true),
                    WasReview = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizAttempts_QuizQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "QuizQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QuestionId = table.Column<int>(type: "INTEGER", nullable: false),
                    EaseFactor = table.Column<double>(type: "REAL", nullable: false),
                    IntervalMinutes = table.Column<double>(type: "REAL", nullable: false),
                    Repetitions = table.Column<int>(type: "INTEGER", nullable: false),
                    DueAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Lapses = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewItems_QuizQuestions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "QuizQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InterviewTurns_SessionId_Sequence",
                table: "InterviewTurns",
                columns: new[] { "SessionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_TopicKey_SubtopicKey",
                table: "Lessons",
                columns: new[] { "TopicKey", "SubtopicKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PronunciationTerms_Term",
                table: "PronunciationTerms",
                column: "Term",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_QuestionId",
                table: "QuizAttempts",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_QuizAttempts_TopicKey_CreatedAt",
                table: "QuizAttempts",
                columns: new[] { "TopicKey", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestions_TopicKey_Level",
                table: "QuizQuestions",
                columns: new[] { "TopicKey", "Level" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_DueAt",
                table: "ReviewItems",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_QuestionId",
                table: "ReviewItems",
                column: "QuestionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InterviewTurns");

            migrationBuilder.DropTable(
                name: "Lessons");

            migrationBuilder.DropTable(
                name: "PlanTasks");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropTable(
                name: "PronunciationTerms");

            migrationBuilder.DropTable(
                name: "QuizAttempts");

            migrationBuilder.DropTable(
                name: "ReviewItems");

            migrationBuilder.DropTable(
                name: "InterviewSessions");

            migrationBuilder.DropTable(
                name: "QuizQuestions");
        }
    }
}
